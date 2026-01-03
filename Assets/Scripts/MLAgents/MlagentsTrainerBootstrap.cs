using UnityEngine;

public class MlagentsTrainerBootstrap : MonoBehaviour
{
    static bool started;

    void Awake()
    {
        // Avoid duplicate bootstraps across scene loads
        if (started) { Destroy(gameObject); return; }
        started = true;
        DontDestroyOnLoad(gameObject);

#if UNITY_EDITOR
        // In the Editor, this is still less reliable than the Editor hook,
        // but it will work most of the time if you insist.
#endif

        MlagentsTrainerLauncher.StartTrainer();
    }

    void OnApplicationQuit()
    {
        MlagentsTrainerLauncher.StopTrainer();
    }

    void OnDisable()
    {
        // Covers "stop play mode" cases where OnApplicationQuit might not fire
#if UNITY_EDITOR
        MlagentsTrainerLauncher.StopTrainer();
#endif
    }
}
