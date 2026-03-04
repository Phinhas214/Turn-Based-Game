using TMPro;
using UnityEngine;

public class DamageNumber : MonoBehaviour
{
    public TextMeshPro text;
    public float lifetime = 0.6f;
    public Vector3 floatVelocity = new Vector3(0, 1f, 0);

    public void Initialize(int amount)
    {
        text.text = amount.ToString();
    }
}