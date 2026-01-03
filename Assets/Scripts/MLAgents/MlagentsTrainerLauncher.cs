using System;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

public static class MlagentsTrainerLauncher
{
    // Change these to match your setup
    const string EnvName = "mlagents";
    const string ConfigRelativePath = "config/ppo_standard.yaml";
    const string RunId = "ppo_standard_1";

    // Point this to your conda.bat (Miniconda/Anaconda)
    // Common locations:
    // C:\Users\<you>\miniconda3\condabin\conda.bat
    // C:\Users\<you>\anaconda3\condabin\conda.bat
    static string CondaBatPath => @"C:\Users\hahm.19\AppData\Local\anaconda3\condabin\conda.bat";

    static Process _proc;

    [MenuItem("ML-Agents/Start PPO Trainer")]
    public static void StartTrainer()
    {
        if (_proc != null && !_proc.HasExited)
        {
            Debug.LogWarning("ML-Agents trainer is already running.");
            return;
        }

        string projectRoot = Directory.GetParent(Application.dataPath)!.FullName;
        string configPath = Path.GetFullPath(Path.Combine(projectRoot, ConfigRelativePath));

        if (!File.Exists(CondaBatPath))
            throw new FileNotFoundException($"conda.bat not found at: {CondaBatPath}");

        if (!File.Exists(configPath))
            throw new FileNotFoundException($"Config not found at: {configPath}");

        // Use conda run so we don't need to "activate" anything.
        // We call conda.bat via cmd.exe.
        string args =
            $"/c \"call {Q(CondaBatPath)} run -n {EnvName} " +
            $"mlagents-learn {Q(configPath)} --run-id {RunId} --force\"";

        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = args,
            WorkingDirectory = projectRoot,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        _proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        _proc.OutputDataReceived += (_, e) => { if (!string.IsNullOrEmpty(e.Data)) Debug.Log(e.Data); };
        _proc.ErrorDataReceived += (_, e) => { if (!string.IsNullOrEmpty(e.Data)) Debug.LogError(e.Data); };
        _proc.Exited += (_, __) => Debug.Log("ML-Agents trainer exited.");

        _proc.Start();
        _proc.BeginOutputReadLine();
        _proc.BeginErrorReadLine();

        Debug.Log("Started ML-Agents trainer.");
    }



    [MenuItem("ML-Agents/Stop PPO Trainer")]
    public static void StopTrainer()
    {
        if (_proc == null)
        {
            Debug.Log("No ML-Agents trainer process tracked.");
            return;
        }

        if (_proc.HasExited)
        {
            Debug.Log("Trainer already exited.");
            _proc.Dispose();
            _proc = null;
            return;
        }

        try
        {
            // cmd.exe is the parent; ensure the whole tree dies (cmd + python).
            KillProcessTree(_proc.Id);
            Debug.Log("Stopped ML-Agents trainer.");
        }
        finally
        {
            _proc.Dispose();
            _proc = null;
        }
    }

    static void KillProcessTree(int pid)
    {
        // Windows process-tree kill
        var psi = new ProcessStartInfo
        {
            FileName = "taskkill",
            Arguments = $"/PID {pid} /T /F",
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var p = Process.Start(psi);
        p?.WaitForExit();
    }

    static string Q(string s) => $"\"{s}\"";
}
