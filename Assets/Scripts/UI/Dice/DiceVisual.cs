using UnityEngine;

public class DiceVisual : MonoBehaviour
{
    public float moveSpeed = 10f;
    public float settleThreshold = 1f;

    DiceSpinVisual spinner;
    Vector3 targetLocalPosition;

    bool hasSettled = false;

    int value;
    TMPro.TextMeshPro valueText;

    void Awake()
    {
        spinner = GetComponent<DiceSpinVisual>();
        valueText = GetComponentInChildren<TMPro.TextMeshPro>(true);
    }

    void SetValue(int newValue)
    {
        value = newValue;

        if (valueText != null)
            valueText.text = newValue.ToString();
    }

    // Called when spawned
    public void Initialize(Vector3 spawnPos, Vector3 settlePos, int rolledValue)
    {
        transform.localPosition = spawnPos;
        targetLocalPosition = settlePos;

        hasSettled = false;

        SetValue(rolledValue);

        if (valueText != null)
            valueText.gameObject.SetActive(false);

        spinner?.Spin();
    }

    public void UpdateTarget(Vector3 newTargetLocalPos)
    {
        targetLocalPosition = newTargetLocalPos;
        hasSettled = false;

        if (valueText != null)
            valueText.gameObject.SetActive(false);
    }

    void Update()
    {
        if (hasSettled)
            return;

        transform.localPosition = Vector3.Lerp(
            transform.localPosition,
            targetLocalPosition,
            Time.deltaTime * moveSpeed
        );

        if (Vector3.Distance(transform.localPosition, targetLocalPosition) < settleThreshold)
        {
            Settle();
        }
    }

    void Settle()
    {
        hasSettled = true;
        transform.localPosition = targetLocalPosition;

        if (valueText != null)
            valueText.gameObject.SetActive(true);
    }

    public void Reroll(int newValue)
    {
        hasSettled = false;

        SetValue(newValue);

        if (valueText != null)
            valueText.gameObject.SetActive(false);

        spinner?.Spin();
    }
}
