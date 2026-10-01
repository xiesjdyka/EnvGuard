// Narrow, version-audited Claude Cowork adapter; no generic VM termination.
// Service path is supplied from the locally confirmed profile, never embedded.
// Never stops vmcompute, vmms, WSL, vmmem, or unrelated compute systems.
// Identity rules were narrowly verified against Claude MSIX 2.16120.0.0,
// cowork-svc.exe SHA-256 B657BE92959A67238FD8A8ED1EF01D9BA91B9C60E2E575517C39AB5258D1E100.
// Its vm.isOurVM accepts Id OR Owner: cowork-vm, cowork-vm-store, or
// cowork-vm- followed by exactly 8 LOWERCASE hex characters. Updates are rejected.
// API docs: learn.microsoft.com/en-us/virtualization/api/hcs/reference/
// hcsenumeratecomputesystems, hcsopencomputesystem, hcsterminatecomputesystem,
// hcswaitforoperationresult. Result strings require LocalFree, not FreeHGlobal.
// This helper never changes networking. EnvGuard provides warnings, not isolation.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace ClaudeDesktopGuard.NativeCompute
{
    public sealed class GuardComputeRecord
    {
        public string Id { get; set; }
        public string Owner { get; set; }
        public string State { get; set; }
        public string SystemType { get; set; }
        public bool IsCowork { get; set; }
        public bool UnknownClaudeIdentity { get; set; }
    }

    public sealed class GuardComputeStopResult
    {
        public bool Success { get; set; }
        public bool SupportedServiceImage { get; set; }
        public long ElapsedMs { get; set; }
        public List<string> TerminatedIds { get; set; }
        public List<string> Errors { get; set; }
        public GuardComputeRecord[] Remaining { get; set; }
        public GuardComputeStopResult()
        {
            TerminatedIds = new List<string>();
            Errors = new List<string>();
            Remaining = new GuardComputeRecord[0];
        }
    }

    public static class GuardComputeSystems
    {
        public static string SupportedServicePath { get; internal set; }
        public const string SupportedServiceSha256 = "B657BE92959A67238FD8A8ED1EF01D9BA91B9C60E2E575517C39AB5258D1E100";
        private const uint GenericAll = 0x10000000;
        private static readonly Regex CurrentIdentity = new Regex(@"\Acowork-vm-[0-9a-f]{8}\z", RegexOptions.CultureInvariant);
        private static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = 4194304, RecursionLimit = 64 };

        [DllImport("computecore.dll", ExactSpelling = true)]
        private static extern IntPtr HcsCreateOperation(IntPtr context, IntPtr callback);
        [DllImport("computecore.dll", ExactSpelling = true)]
        private static extern void HcsCloseOperation(IntPtr operation);
        [DllImport("computecore.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern int HcsEnumerateComputeSystems(string query, IntPtr operation);
        [DllImport("computecore.dll", ExactSpelling = true)]
        private static extern int HcsWaitForOperationResult(IntPtr operation, uint timeoutMs, out IntPtr result);
        [DllImport("computecore.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern int HcsOpenComputeSystem(string id, uint access, out IntPtr system);
        [DllImport("computecore.dll", ExactSpelling = true)]
        private static extern void HcsCloseComputeSystem(IntPtr system);
        [DllImport("computecore.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern int HcsGetComputeSystemProperties(IntPtr system, IntPtr operation, string query);
        [DllImport("computecore.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern int HcsTerminateComputeSystem(IntPtr system, IntPtr operation, string options);
        [DllImport("kernel32.dll", ExactSpelling = true)]
        private static extern IntPtr LocalFree(IntPtr memory);

        public static bool IsKnownCoworkIdentity(string value)
        {
            return value == "cowork-vm" || value == "cowork-vm-store" ||
                (value != null && CurrentIdentity.IsMatch(value));
        }

        private static bool LooksLikeUnknownClaude(string value)
        {
            return !String.IsNullOrEmpty(value) &&
                (value.IndexOf("cowork", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 value.IndexOf("claude", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static string TextField(IDictionary<string, object> row, string key, bool required)
        {
            object value;
            if (!row.TryGetValue(key, out value) || value == null)
            {
                if (required) throw new InvalidDataException("HCS schema: missing " + key);
                return null;
            }
            string text = value as string;
            if (text == null || text.Length > 4096 || text.IndexOf('\0') >= 0 || (required && text.Length == 0))
                throw new InvalidDataException("HCS schema: invalid " + key);
            return text;
        }

        private static GuardComputeRecord ParseRow(object rowObject)
        {
            IDictionary<string, object> row = rowObject as IDictionary<string, object>;
            if (row == null) throw new InvalidDataException("HCS schema: expected system object");
            GuardComputeRecord record = new GuardComputeRecord();
            record.Id = TextField(row, "Id", true);
            record.Owner = TextField(row, "Owner", false);
            record.State = TextField(row, "State", false);
            record.SystemType = TextField(row, "SystemType", false);
            record.IsCowork = IsKnownCoworkIdentity(record.Id) || IsKnownCoworkIdentity(record.Owner);
            record.UnknownClaudeIdentity = !record.IsCowork &&
                (LooksLikeUnknownClaude(record.Id) || LooksLikeUnknownClaude(record.Owner));
            return record;
        }

        public static GuardComputeRecord[] ParseEnumeration(string document)
        {
            if (String.IsNullOrWhiteSpace(document)) throw new InvalidDataException("HCS returned no enumeration document");
            object decoded = Json.DeserializeObject(document);
            object[] rows = decoded as object[];
            if (rows == null) throw new InvalidDataException("HCS schema: expected a top-level array");
            if (rows.Length > 4096) throw new InvalidDataException("HCS schema: unreasonable system count");
            List<GuardComputeRecord> result = new List<GuardComputeRecord>();
            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (object row in rows)
            {
                GuardComputeRecord record = ParseRow(row);
                if (!ids.Add(record.Id)) throw new InvalidDataException("HCS schema: duplicate system Id");
                result.Add(record);
            }
            return result.ToArray();
        }

        private static void CheckResult(int hr, string step)
        {
            if (hr != 0)
            {
                uint value = unchecked((uint)hr);
                string detail = value == 0x8037011B ? " (HCS_E_ACCESS_DENIED; elevation required)" :
                    value == 0x80370118 ? " (HCS_E_OPERATION_TIMEOUT)" :
                    value == 0x8037010E ? " (HCS_E_SYSTEM_NOT_FOUND)" : "";
                throw new InvalidOperationException(step + " failed HRESULT 0x" + value.ToString("X8") + detail);
            }
        }

        private static IntPtr CreateOperation()
        {
            IntPtr operation = HcsCreateOperation(IntPtr.Zero, IntPtr.Zero);
            if (operation == IntPtr.Zero) throw new InvalidOperationException("HcsCreateOperation returned NULL");
            return operation;
        }

        private static string WaitResult(IntPtr operation, int timeoutMs, string step)
        {
            IntPtr buffer = IntPtr.Zero;
            try
            {
                int hr = HcsWaitForOperationResult(operation, checked((uint)timeoutMs), out buffer);
                // Do not print an arbitrary HCS diagnostic body; it can contain host paths.
                CheckResult(hr, step + ": wait (timeout " + timeoutMs + "ms)");
                return buffer == IntPtr.Zero ? null : Marshal.PtrToStringUni(buffer);
            }
            finally { if (buffer != IntPtr.Zero) LocalFree(buffer); }
        }

        public static GuardComputeRecord[] Enumerate(int timeoutMs)
        {
            if (timeoutMs < 1 || timeoutMs > 30000) throw new ArgumentOutOfRangeException("timeoutMs");
            IntPtr operation = CreateOperation();
            try
            {
                CheckResult(HcsEnumerateComputeSystems(null, operation), "HcsEnumerateComputeSystems");
                return ParseEnumeration(WaitResult(operation, timeoutMs, "HcsEnumerateComputeSystems"));
            }
            finally { HcsCloseOperation(operation); }
        }

        public static void VerifySupportedServiceImage()
        {
            using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\CoworkVMService"))
            {
                if (key == null) throw new InvalidOperationException("CoworkVMService is missing; identity version cannot be validated");
                string path = key.GetValue("ImagePath") as string;
                if (path == null || !String.Equals(path.Trim().Trim('"'), SupportedServicePath, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Cowork service path/version changed; stop matcher requires a new audit");
            }
            string actual;
            using (SHA256 hash = SHA256.Create())
            using (FileStream stream = File.OpenRead(SupportedServicePath))
                actual = BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "");
            if (!String.Equals(actual, SupportedServiceSha256, StringComparison.Ordinal))
                throw new InvalidOperationException("Cowork service binary changed; stop matcher requires a new audit");
        }

        private static void RequireStoppedCoworkService()
        {
            using (ServiceController service = new ServiceController("CoworkVMService"))
            {
                service.Refresh();
                if (service.Status != ServiceControllerStatus.Stopped)
                    throw new InvalidOperationException("CoworkVMService must already be stopped, preventing guest respawn; this helper does not stop services");
            }
        }

        private static int RemainingMs(Stopwatch clock, int budget)
        {
            long remaining = budget - clock.ElapsedMilliseconds;
            if (remaining <= 0) throw new System.TimeoutException("Cowork termination/verification deadline exceeded; closure not verified");
            return (int)Math.Min(remaining, 2000);
        }

        private static GuardComputeRecord QueryOpenedSystem(IntPtr system, int timeoutMs)
        {
            IntPtr operation = CreateOperation();
            try
            {
                CheckResult(HcsGetComputeSystemProperties(system, operation, null), "HcsGetComputeSystemProperties");
                string document = WaitResult(operation, timeoutMs, "HcsGetComputeSystemProperties");
                if (String.IsNullOrWhiteSpace(document)) throw new InvalidDataException("HCS property result was empty");
                return ParseRow(Json.DeserializeObject(document));
            }
            finally { HcsCloseOperation(operation); }
        }

        public static GuardComputeStopResult StopCowork(int budgetMs, bool explicitlyAuthorized)
        {
            GuardComputeStopResult result = new GuardComputeStopResult();
            Stopwatch clock = Stopwatch.StartNew();
            try
            {
                if (!explicitlyAuthorized) throw new UnauthorizedAccessException("Explicit --stop-cowork authorization required");
                if (budgetMs < 1000 || budgetMs > 30000) throw new ArgumentOutOfRangeException("budgetMs");
                using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                    if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
                        throw new UnauthorizedAccessException("Elevated administrator token required");
                VerifySupportedServiceImage();
                result.SupportedServiceImage = true;
                RequireStoppedCoworkService();
                int consecutiveEmpty = 0;
                while (true)
                {
                    GuardComputeRecord[] all = Enumerate(RemainingMs(clock, budgetMs));
                    List<GuardComputeRecord> targets = new List<GuardComputeRecord>();
                    foreach (GuardComputeRecord record in all)
                    {
                        if (record.UnknownClaudeIdentity)
                            throw new InvalidDataException("Unrecognized Claude/Cowork compute identity; refusing broad termination or successful verification");
                        if (record.IsCowork) targets.Add(record);
                    }
                    result.Remaining = targets.ToArray();
                    if (targets.Count == 0)
                    {
                        consecutiveEmpty++;
                        if (consecutiveEmpty >= 2)
                        {
                            RequireStoppedCoworkService();
                            result.Success = result.Errors.Count == 0;
                            break;
                        }
                    }
                    else
                    {
                        consecutiveEmpty = 0;
                        foreach (GuardComputeRecord target in targets)
                        {
                            IntPtr system = IntPtr.Zero;
                            IntPtr operation = IntPtr.Zero;
                            try
                            {
                                CheckResult(HcsOpenComputeSystem(target.Id, GenericAll, out system), "HcsOpenComputeSystem");
                                if (system == IntPtr.Zero) throw new InvalidOperationException("HcsOpenComputeSystem returned NULL");
                                GuardComputeRecord reopened = QueryOpenedSystem(system, RemainingMs(clock, budgetMs));
                                if (!String.Equals(reopened.Id, target.Id, StringComparison.Ordinal) || !reopened.IsCowork || reopened.UnknownClaudeIdentity)
                                    throw new InvalidDataException("Opened compute system identity did not match the validated Cowork target");
                                operation = CreateOperation();
                                CheckResult(HcsTerminateComputeSystem(system, operation, null), "HcsTerminateComputeSystem");
                                WaitResult(operation, RemainingMs(clock, budgetMs), "HcsTerminateComputeSystem");
                                result.TerminatedIds.Add(target.Id);
                            }
                            catch (Exception targetError)
                            {
                                // Failure for one guest must not prevent attempts to stop the
                                // other verified Cowork guests. Any such error still makes the
                                // overall call fail, even if a later re-enumeration is empty.
                                string message = target.Id + ": " + targetError.GetType().Name + ": " + targetError.Message;
                                if (!result.Errors.Contains(message)) result.Errors.Add(message);
                            }
                            finally
                            {
                                if (operation != IntPtr.Zero) HcsCloseOperation(operation);
                                if (system != IntPtr.Zero) HcsCloseComputeSystem(system);
                            }
                        }
                    }
                    Thread.Sleep(Math.Min(200, RemainingMs(clock, budgetMs)));
                }
            }
            catch (Exception error)
            {
                result.Success = false;
                result.Errors.Add(error.GetType().Name + ": " + error.Message);
            }
            finally { result.ElapsedMs = clock.ElapsedMilliseconds; }
            return result;
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Self-test failed: " + message);
        }

        public static int RunSelfTests()
        {
            string[] accepted = { "cowork-vm", "cowork-vm-store", "cowork-vm-0123abcd", "cowork-vm-ffffffff" };
            string[] rejected = { null, "", "vmmem", "WSL", "DockerDesktopVM", "cowork-vm-0123ABCD", "cowork-vm-0123abcd\n", "cowork-vm-0123abc", "cowork-vm-0123abcde", "cowork-vm-store-0123abcd", "other-cowork-vm", "claude" };
            foreach (string value in accepted) Assert(IsKnownCoworkIdentity(value), "accepted identity");
            foreach (string value in rejected) Assert(!IsKnownCoworkIdentity(value), "rejected identity");
            GuardComputeRecord[] records = ParseEnumeration("[{\"Id\":\"cowork-vm-0123abcd\",\"Owner\":\"x\",\"State\":\"Running\"},{\"Id\":\"opaque\",\"Owner\":\"cowork-vm-store\"},{\"Id\":\"unrelated\",\"Owner\":\"WSL\"},{\"Id\":\"cowork-vm-newformat\"}]");
            Assert(records.Length == 4 && records[0].IsCowork && records[1].IsCowork && !records[2].IsCowork && records[3].UnknownClaudeIdentity, "Id and Owner classification");
            Assert(ParseEnumeration("[]").Length == 0, "empty array");
            int invalidCount = 0;
            foreach (string bad in new[] { "", "null", "{}", "[null]", "[{\"Owner\":\"x\"}]", "[{\"Id\":1}]", "[{\"Id\":\"x\",\"Owner\":{}}]", "[{\"Id\":\"x\"},{\"Id\":\"x\"}]" })
                try { ParseEnumeration(bad); } catch { invalidCount++; }
            Assert(invalidCount == 8, "malformed and future schema rejection");
            GuardComputeStopResult denied = StopCowork(1000, false);
            Assert(!denied.Success && denied.Errors.Count == 1 && denied.TerminatedIds.Count == 0, "no native calls without explicit authorization");
            return accepted.Length + rejected.Length + 4;
        }

        public static int Main(string[] args)
        {
            try
            {
                if (args.Length == 1 && args[0] == "--self-test")
                {
                    Console.WriteLine(Json.Serialize(new { Success = true, OfflineOnly = true, Tests = RunSelfTests() }));
                    return 0;
                }
                if (args.Length == 1 && args[0] == "--stop-cowork")
                {
                    GuardComputeStopResult stopped = StopCowork(15000, true);
                    Console.WriteLine(Json.Serialize(stopped));
                    return stopped.Success ? 0 : 2;
                }
                if (args.Length != 0 && !(args.Length == 1 && args[0] == "--enumerate"))
                    throw new ArgumentException("Usage: GuardCompute.exe [--enumerate | --self-test | --stop-cowork]");
                GuardComputeRecord[] systems = Enumerate(3000);
                Console.WriteLine(Json.Serialize(new { Success = true, ReadOnly = true, Systems = systems }));
                return 0;
            }
            catch (Exception error)
            {
                Console.WriteLine(Json.Serialize(new { Success = false, Error = error.GetType().Name + ": " + error.Message }));
                return 2;
            }
        }
    }
}
