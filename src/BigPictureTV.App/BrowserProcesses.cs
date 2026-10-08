using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Management;
using BigPictureTV.Core;
using BigPictureTV.Core.TvMenu;

namespace BigPictureTV.App;

/// <summary>
/// Finds the browser a web tile opened, by its profile folder. The process
/// we start often hands over to another one and quits, so its id alone
/// can't tell when the page closes or how to close it.
/// </summary>
static class BrowserProcesses
{
    /// <summary>The browser's main processes using this profile (normally one).</summary>
    public static List<Process> Find(string browserName, string profileDir, ILog log)
    {
        var found = new List<Process>();
        try
        {
            string name = browserName.Replace("'", "") + ".exe";
            using var searcher = new ManagementObjectSearcher(
                $"SELECT ProcessId, CommandLine FROM Win32_Process WHERE Name = '{name}'");
            using var results = searcher.Get();
            foreach (ManagementBaseObject row in results)
            {
                using (row)
                {
                    if (!BrowserCommand.IsMainProcessFor(row["CommandLine"] as string, profileDir)) continue;
                    try { found.Add(Process.GetProcessById(Convert.ToInt32(row["ProcessId"]))); }
                    catch (ArgumentException) { } // already gone
                }
            }
        }
        catch (Exception e) when (e is ManagementException or System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
        {
            log.Write($"Looking for the browser failed: {e.Message}");
        }
        return found;
    }

    /// <summary>Command line and parent of a process, or nulls if it's gone or hidden.</summary>
    public static (string? CommandLine, int Parent) Describe(int processId)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                $"SELECT CommandLine, ParentProcessId FROM Win32_Process WHERE ProcessId = {processId}");
            using var results = searcher.Get();
            foreach (ManagementBaseObject row in results)
                using (row)
                    return (row["CommandLine"] as string, Convert.ToInt32(row["ParentProcessId"]));
        }
        catch (Exception e) when (e is ManagementException or System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
        {
        }
        return (null, 0);
    }

    /// <summary>Closes the browser on this profile: politely first, so it keeps logins, then by force.</summary>
    public static void Close(string browserName, string profileDir, ILog log)
    {
        foreach (var process in Find(browserName, profileDir, log))
        {
            using (process)
            {
                try
                {
                    if (!process.HasExited && (!process.CloseMainWindow() || !process.WaitForExit(3000)))
                        process.Kill(entireProcessTree: true);
                }
                catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    log.Write($"Closing the browser failed: {e.Message}");
                }
            }
        }
    }
}
