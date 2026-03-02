// SpawnPoint.cs
using UnityEngine;

/// <summary>
/// Place on a GameObject inside a room prefab to mark where the player
/// spawns when entering from a specific direction.
/// The GameObject should be positioned at the CENTER of the spawn tile.
/// </summary>
public class SpawnPoint : MonoBehaviour
{
    [Tooltip("Which direction the player is coming FROM when using this spawn point.\n" +
             "e.g. 'South' means the player entered through the south door.")]
    public LevelGenerator.Direction entryDirection;

    private void OnDrawGizmos()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawSphere(transform.position, 0.3f);
        Gizmos.color = Color.white;
#if UNITY_EDITOR
        UnityEditor.Handles.Label(transform.position + Vector3.up * 0.5f, 
            $"Spawn: From {entryDirection}");
#endif
    }
}