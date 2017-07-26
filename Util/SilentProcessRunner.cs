﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
#if CAN_FIND_CHILD_PROCESSES
using System.Management;
#endif
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Octopus.Diagnostics;
using Octopus.Shared.Diagnostics;

namespace Octopus.Shared.Util
{
    public static class SilentProcessRunner
    {
        // ReSharper disable once InconsistentNaming
        const int CP_OEMCP = 1;
        static readonly Encoding oemEncoding;
        static readonly ILog SystemLog = Log.System();

        static SilentProcessRunner()
        {
            try
            {
                CPINFOEX info;
                if (GetCPInfoEx(CP_OEMCP, 0, out info))
                {
                    oemEncoding = Encoding.GetEncoding(info.CodePage);
                }
                else
                {
                    oemEncoding = Encoding.GetEncoding(850);
                }
            }
            catch (Exception ex)
            {
                Log.Octopus().Warn(ex, "Couldn't get default OEM encoding");
                oemEncoding = Encoding.UTF8;
            }
        }

        public static int ExecuteCommand(this CommandLineInvocation invocation, ILog log)
        {
            return ExecuteCommand(invocation, Environment.CurrentDirectory, log);
        }

        public static int ExecuteCommand(this CommandLineInvocation invocation, string workingDirectory, ILog log)
        {
            var arguments = (invocation.Arguments ?? "") + " " + (invocation.SystemArguments ?? "");

            var exitCode = ExecuteCommand(
                invocation.Executable,
                arguments,
                workingDirectory,
                log.Info,
                log.Error
                );

            return exitCode;
        }

        public static CmdResult ExecuteCommand(this CommandLineInvocation invocation)
        {
            return ExecuteCommand(invocation, Environment.CurrentDirectory);
        }

        public static CmdResult ExecuteCommand(this CommandLineInvocation invocation, string workingDirectory)
        {
            var arguments = (invocation.Arguments ?? "") + " " + (invocation.SystemArguments ?? "");
            var infos = new List<string>();
            var errors = new List<string>();

            var exitCode = ExecuteCommand(
                invocation.Executable,
                arguments,
                workingDirectory,
                infos.Add,
                errors.Add
                );

            return new CmdResult(exitCode, infos, errors);
        }

        public static int ExecuteCommand(string executable, string arguments, string workingDirectory, Action<string> output, Action<string> error)
        {
            return ExecuteCommand(executable, arguments, workingDirectory, output, error, CancellationToken.None);
        }

        public static int ExecuteCommand(string executable, string arguments, string workingDirectory, Action<string> output, Action<string> error, CancellationToken cancel)
        {
            try
            {
                // We need to be careful to make sure the message is accurate otherwise people could wrongly assume the exe is in the working directory when it could be somewhere completely different!
                var exeInSamePathAsWorkingDirectory = string.Equals(Path.GetDirectoryName(executable).TrimEnd('\\', '/'), workingDirectory.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
                var exeFileNameOrFullPath = exeInSamePathAsWorkingDirectory ? Path.GetFileName(executable) : executable;
                SystemLog.Info($"Starting {exeFileNameOrFullPath} in {workingDirectory}");
                using (var process = new Process())
                {
                    process.StartInfo.FileName = executable;
                    process.StartInfo.Arguments = arguments;
                    process.StartInfo.WorkingDirectory = workingDirectory;
                    process.StartInfo.UseShellExecute = false;
                    process.StartInfo.CreateNoWindow = true;
                    process.StartInfo.RedirectStandardOutput = true;
                    process.StartInfo.RedirectStandardError = true;
                    process.StartInfo.StandardOutputEncoding = oemEncoding;
                    process.StartInfo.StandardErrorEncoding = oemEncoding;

                    using (var outputWaitHandle = new AutoResetEvent(false))
                    using (var errorWaitHandle = new AutoResetEvent(false))
                    {
                        process.OutputDataReceived += (sender, e) =>
                        {
                            try
                            {
                                if (e.Data == null)
                                    outputWaitHandle.Set();
                                else
                                    output(e.Data);
                            }
                            catch (Exception ex)
                            {
                                try
                                {
                                    error($"Error occured handling message: {ex.PrettyPrint()}");
                                }
                                catch
                                {
                                    // Ignore
                                }
                            }
                        };

                        process.ErrorDataReceived += (sender, e) =>
                        {
                            try
                            {
                                if (e.Data == null)
                                    errorWaitHandle.Set();
                                else
                                    error(e.Data);
                            }
                            catch (Exception ex)
                            {
                                try
                                {
                                    error($"Error occured handling message: {ex.PrettyPrint()}");
                                }
                                catch
                                {
                                    // Ignore
                                }
                            }
                        };

                        process.Start();

                        var running = true;

                        cancel.Register(() =>
                        {
                            if (!running)
                                return;
                            DoOurBestToCleanUp(process);
                        });

                        if (cancel.IsCancellationRequested)
                        {
                            DoOurBestToCleanUp(process);
                        }

                        process.BeginOutputReadLine();
                        process.BeginErrorReadLine();

                        process.WaitForExit();

                        SystemLog.Info($"Process {exeFileNameOrFullPath} in {workingDirectory} exited with code {process.ExitCode}");

                        running = false;

                        outputWaitHandle.WaitOne();
                        errorWaitHandle.WaitOne();

                        return process.ExitCode;
                    }
                }
            }
            catch (Exception ex)
            {
                throw new Exception(string.Format("Error when attempting to execute {0}: {1}", executable, ex.Message), ex);
            }
        }

        public static void ExecuteBackgroundCommand(string executable, string arguments, string workingDirectory, string username = null, string password = null)
        {
            try
            {
                using (var process = new Process())
                {
                    process.StartInfo.FileName = executable;
                    process.StartInfo.Arguments = arguments;
                    process.StartInfo.WorkingDirectory = workingDirectory;
                    process.StartInfo.UseShellExecute = false;
                    process.StartInfo.CreateNoWindow = true;

                    if (username != null)
                    {
                        process.StartInfo.UserName = username;
                        process.StartInfo.Password = new NetworkCredential("", password ?? "").SecurePassword;
                    }

                    process.Start();
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Error when attempting to execute {executable}: {ex.Message}", ex);
            }
        }

        static void DoOurBestToCleanUp(Process process)
        {
            try
            {
                KillProcessAndChildren(process.Id);
            }
            catch (Exception)
            {
                try
                {
                    process.Kill();
                }
                catch (Exception)
                {
                }
            }
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool GetCPInfoEx([MarshalAs(UnmanagedType.U4)] int CodePage, [MarshalAs(UnmanagedType.U4)] int dwFlags, out CPINFOEX lpCPInfoEx);

        const int MAX_DEFAULTCHAR = 2;
        const int MAX_LEADBYTES = 12;
        const int MAX_PATH = 260;

        [StructLayout(LayoutKind.Sequential)]
        struct CPINFOEX
        {
            [MarshalAs(UnmanagedType.U4)] public readonly int MaxCharSize;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = MAX_DEFAULTCHAR)] public readonly byte[] DefaultChar;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = MAX_LEADBYTES)] public readonly byte[] LeadBytes;

            public readonly char UnicodeDefaultChar;

            [MarshalAs(UnmanagedType.U4)] public readonly int CodePage;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = MAX_PATH)] public readonly string CodePageName;
        }

        static void KillProcessAndChildren(int pid)
        {
#if CAN_FIND_CHILD_PROCESSES
            using (var searcher = new ManagementObjectSearcher("Select * From Win32_Process Where ParentProcessID=" + pid))
            {
                using (var moc = searcher.Get())
                {
                    foreach (var mo in moc.OfType<ManagementObject>())
                    {
                        KillProcessAndChildren(Convert.ToInt32(mo["ProcessID"]));
                    }
                }
            }
#endif

            try
            {
                var proc = Process.GetProcessById(pid);
                proc.Kill();
            }
            catch (ArgumentException)
            {
                // Process already exited.
            }
        }
    }
}