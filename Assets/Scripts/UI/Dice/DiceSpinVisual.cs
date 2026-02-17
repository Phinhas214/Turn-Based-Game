using UnityEngine;
using System.Collections;

public class DiceSpinVisual : MonoBehaviour
{
    public float spinDuration = 0.5f;
    public float spinSpeed = 720f;

    Quaternion startRotation;
    Coroutine spinRoutine;

    void Awake()
    {
        startRotation = transform.localRotation;
    }

    public void Spin()
    {
        if (spinRoutine != null)
            StopCoroutine(spinRoutine);

        spinRoutine = StartCoroutine(SpinRoutine());
    }

    IEnumerator SpinRoutine()
    {
        float elapsed = 0f;
        Vector3 axis = Random.onUnitSphere;

        while (elapsed < spinDuration)
        {
            float dt = Time.deltaTime;
            elapsed += dt;

            transform.Rotate(axis, spinSpeed * dt, Space.Self);
            yield return null;
        }

        // settle back
        transform.localRotation = startRotation;
        spinRoutine = null;
    }
}
