using UnityEngine;

public class PlayerSetupChecker : MonoBehaviour
{
    private void Start()
    {
        Debug.Log("=== PLAYER SETUP CHECK ===");

        // Check Unit
        Unit unit = GetComponent<Unit>();
        Debug.Log($"Unit: {(unit != null ? "✅ Found" : "❌ MISSING")}");

        if (unit != null)
        {
            Debug.Log($"  Initialized: {unit.IsInitialized()}");
            Debug.Log($"  Has room grid: {unit.GetCurrentRoomGrid() != null}");
        }

        // Check MoveAction
        MoveAction move = GetComponent<MoveAction>();
        Debug.Log($"MoveAction: {(move != null ? "✅ Found" : "❌ MISSING")}");

        // Check Collider - fixed to work with ANY collider type
        Collider col = GetComponent<Collider>();
        Debug.Log($"Collider: {(col != null ? "✅ Found - " + col.GetType().Name : "❌ MISSING - ADD ONE!")}");

        if (col != null)
        {
            // This now works for BoxCollider, CapsuleCollider, SphereCollider etc
            Debug.Log($"  Is Trigger: {col.isTrigger}");

            if (col.isTrigger)
            {
                Debug.LogWarning("  ⚠️ Collider Is Trigger is ON!" +
                                 " Turn it OFF or raycasts won't detect the player!");
            }
            else
            {
                Debug.Log("  ✅ Is Trigger is OFF - correct!");
            }
        }

        // Check Layer
        string layerName = LayerMask.LayerToName(gameObject.layer);
        Debug.Log($"Layer: '{layerName}' (index: {gameObject.layer})");

        if (layerName == "Units")
        {
            Debug.Log("  ✅ Correct layer!");
        }
        else
        {
            Debug.LogWarning($"  ❌ Wrong layer! Should be 'Unit' but is '{layerName}'");
        }

        // Check UnitActionSystem
        UnitActionSystem uas = FindFirstObjectByType<UnitActionSystem>();
        Debug.Log($"UnitActionSystem: {(uas != null ? "✅ Found" : "❌ NOT FOUND IN SCENE")}");

        // Check MouseWorld
        MouseWorld mw = FindFirstObjectByType<MouseWorld>();
        Debug.Log($"MouseWorld: {(mw != null ? "✅ Found" : "❌ MISSING")}");
    }
}