﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace Roslyn.VisualStudio.Test.Utilities
{
    public sealed class VisualStudioInstanceFactory : IDisposable
    {
        internal static readonly string VsProductVersion = Settings.Default.VsProductVersion;
        internal static readonly string VsProgId = $"VisualStudio.DTE.{VsProductVersion}";

        internal static readonly string Wow6432Registry = Environment.Is64BitProcess ? "WOW6432Node" : string.Empty;
        internal static readonly string VsRegistryRoot = Path.Combine("SOFTWARE", Wow6432Registry, "Microsoft", "VisualStudio", VsProductVersion);

        internal static readonly string VsCommon7Folder = Path.GetFullPath(IntegrationHelper.GetRegistryKeyValue(Registry.LocalMachine, VsRegistryRoot, "InstallDir").ToString());

        internal static readonly string VsExeFile = Path.Combine(VsCommon7Folder, "devenv.exe");
        internal static readonly string VsLaunchArgs = $"{(string.IsNullOrWhiteSpace(Settings.Default.VsRootSuffix) ? "/log" : $"/rootsuffix {Settings.Default.VsRootSuffix}")} /log";

        /// <summary>
        /// The instance that has already been launched by this factory and can be reused.
        /// </summary>
        private VisualStudioInstance _currentlyRunningInstance;

        /// <summary>
        /// Returns the running <see cref="VisualStudioInstance"/>, starting one if necessary.
        /// </summary>
        public VisualStudioInstance GetNewOrUsedInstance()
        {
            // If something fails reusing a previous instance, we'll just make a new one
            try
            {
                if (_currentlyRunningInstance != null && _currentlyRunningInstance.IsRunning)
                {
                    _currentlyRunningInstance.CloseAndDeleteOpenSolution();
                    return _currentlyRunningInstance;
                }
            }
            catch (Exception e)
            {
                Debug.WriteLine($"Warning: Failed to cleanup.");
                Debug.WriteLine($"\t{e}");
            }

            return GetNewInstance();
        }

        /// <summary>
        /// Starts up a new <see cref="VisualStudioInstance"/>, shutting down any instances that are already running.
        /// </summary>
        public VisualStudioInstance GetNewInstance()
        {
            // In case the cleanup process fails, preemtively null out _currentlyRunningInstance so we don't reuse this
            var oldInstance = _currentlyRunningInstance;
            _currentlyRunningInstance = null;

            oldInstance?.Close();

            var process = StartNewVisualStudioProcess();

            // We wait until the DTE instance is up before we're good
            var dte = IntegrationHelper.WaitForNotNullAsync(() => IntegrationHelper.TryLocateDteForProcess(process)).Result;

            _currentlyRunningInstance = new VisualStudioInstance(process, dte);

            return _currentlyRunningInstance;
        }

        private Process StartNewVisualStudioProcess()
        {
            // TODO: This might not be needed anymore as I don't believe we do things which risk corrupting the MEF cache. However,
            // it is still useful to do in case some other action corruped the MEF cache as we don't have to restart the host
            Process.Start(VsExeFile, $"/clearcache {VsLaunchArgs}").WaitForExit();
            Process.Start(VsExeFile, $"/updateconfiguration {VsLaunchArgs}").WaitForExit();

            // Make sure we kill any leftover processes spawned by the host
            IntegrationHelper.KillProcess("DbgCLR");
            IntegrationHelper.KillProcess("VsJITDebugger");
            IntegrationHelper.KillProcess("dexplore");

            var process = Process.Start(VsExeFile, VsLaunchArgs);

            Debug.WriteLine($"Launched a new instance of Visual Studio. (ID: {process.Id})");

            return process;
        }

        public void Dispose()
        {
            _currentlyRunningInstance?.Close();
        }
    }
}
