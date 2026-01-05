using System.Collections;
using UnityEngine;
using Unity.MLAgents;

public class PpoManualStepper : MonoBehaviour
{
    [SerializeField, Min(0.1f)]
    private float stepsPerSecond = 5f;

    private Coroutine loop;

    void Awake()
    {
        // Stop ML-Agents from stepping every FixedUpdate
        Academy.Instance.AutomaticSteppingEnabled = false;
    }

    void OnEnable()
    {
        loop = StartCoroutine(StepLoop());
    }

    void OnDisable()
    {
        if (loop != null) StopCoroutine(loop);
        loop = null;
    }

    IEnumerator StepLoop()
    {
        var wait = new WaitForSecondsRealtime(1f / stepsPerSecond);

        while (true)
        {
            Academy.Instance.EnvironmentStep();
            yield return wait;
        }
    }
}
