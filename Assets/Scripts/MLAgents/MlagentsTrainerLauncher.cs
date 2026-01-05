using System;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

public static class MlagentsTrainerLauncher
{
    // Change these to match your setup
    const string EnvName = "mlagents110";
    const string ConfigRelativePath = "config/ppo_standard.yaml";
    const string RunId = "ppo_standard_1";

    // Point this to your conda.bat (Miniconda/Anaconda)
    // Common locations:
    // C:\Users\<you>\miniconda3\condabin\conda.bat
    // C:\Users\<you>\anaconda3\condabin\conda.bat
    static string CondaBatPath => @"C:\Users\hahm.19\AppData\Local\anaconda3\condabin\conda.bat";

    static Process _proc;
    public const int BasePort = 5004;


    static string GetEnvPythonExe()
    {
        // CondaBatPath = ...\anaconda3\condabin\conda.bat
        // python.exe      ...\anaconda3\envs\<EnvName>\python.exe
        var condaRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(CondaBatPath)!, "..")); // condabin -> anaconda3
        return Path.Combine(condaRoot, "envs", EnvName, "python.exe");
    }

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

        var pythonExe = GetEnvPythonExe();
        if (!File.Exists(pythonExe))
            throw new FileNotFoundException($"python.exe not found at: {pythonExe}");

        // -u => unbuffered output so your RedirectStandardOutput shows logs immediately
        string arguments =
            $"-u -m mlagents.trainers.learn \"{configPath}\" " +
            $"--run-id {RunId} --force --base-port {BasePort}";

        Debug.Log($"Starting ML-Agents trainer on base port {BasePort}...");
        Debug.Log($"Trainer python: {pythonExe}");
        Debug.Log($"Trainer args:   {arguments}");

        var psi = new ProcessStartInfo
        {
            FileName = pythonExe,
            Arguments = arguments,
            WorkingDirectory = projectRoot,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        // Extra belt-and-suspenders: ensure unbuffered streams
        psi.EnvironmentVariables["PYTHONUNBUFFERED"] = "1";

        _proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        _proc.OutputDataReceived += (_, e) => { if (!string.IsNullOrEmpty(e.Data)) Debug.Log(e.Data); };
        _proc.ErrorDataReceived += (_, e) => { if (!string.IsNullOrEmpty(e.Data)) Debug.LogWarning(e.Data); };
        _proc.Exited += (_, __) => Debug.Log($"ML-Agents trainer exited. code={_proc.ExitCode}");

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
    public static System.Collections.IEnumerator WaitForTrainerPort(int port, float timeoutSeconds = 20f)
    {
        float start = Time.realtimeSinceStartup;

        while (Time.realtimeSinceStartup - start < timeoutSeconds)
        {
            if (IsPortOpen("127.0.0.1", port, 100))
            {
                Debug.Log($"Trainer port {port} is open.");
                yield break;
            }

            yield return new WaitForSecondsRealtime(0.1f);
        }

        Debug.LogError($"Trainer port {port} did not open within {timeoutSeconds:0.0}s.");
    }

    static bool IsPortOpen(string host, int port, int timeoutMs)
    {
        try
        {
            using var client = new TcpClient();
            var ar = client.BeginConnect(host, port, null, null);
            bool ok = ar.AsyncWaitHandle.WaitOne(timeoutMs);
            if (!ok) return false;
            client.EndConnect(ar);
            return true;
        }
        catch
        {
            return false;
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
