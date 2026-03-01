using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Base class for all tile-based interactions in a room.
/// Inheriting classes can respond to:
/// - Player entering/exiting
/// - Combat starting/ending
/// 
/// Examples: Doors, destructible walls, pressure plates, traps, etc.
/// </summary>
public abstract class TileInteraction : MonoBehaviour
{
    [Header("Grid Position")]
    [SerializeField] protected GridPosition gridPosition;
    
    protected Tilemap wallsTilemap;
    protected Tilemap floorTilemap;
    protected RoomGrid roomGrid;
    
    /// <summary>Call this after placing the interaction to initialize it.</summary>
    public virtual void Initialize(GridPosition pos, RoomGrid room, Tilemap walls, Tilemap floor = null)
    {
        gridPosition = pos;
        roomGrid = room;
        wallsTilemap = walls;
        floorTilemap = floor;
    }
    
    /// <summary>Called when player enters the tile with this interaction.</summary>
    public abstract void OnPlayerEnter();
    
    /// <summary>Called when player exits the tile with this interaction.</summary>
    public abstract void OnPlayerExit();
    
    /// <summary>Called when combat starts in the room.</summary>
    public abstract void OnCombatStart();
    
    /// <summary>Called when combat ends in the room (all enemies dead).</summary>
    public abstract void OnCombatEnd();
    
    protected Vector3Int ToVector3Int(GridPosition gp) => new Vector3Int(gp.x, gp.z, 0);
}

// ════════════════════════════════════════════════════════════════════════════════
// EXAMPLE IMPLEMENTATIONS
// ════════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Door that locks when combat starts, unlocks when combat ends.
/// Blocks player movement while locked (collision enabled).
/// </summary>
public class CombatDoor : TileInteraction
{
    [Header("Door Visuals")]
    [SerializeField] private SpriteRenderer spriteRenderer;
    [SerializeField] private Sprite lockedSprite;
    [SerializeField] private Sprite unlockedSprite;
    
    [Header("Door Physics")]
    [SerializeField] private Collider2D doorCollider;
    
    private bool isLocked = false;
    
    public override void OnPlayerEnter() { /* nothing */ }
    public override void OnPlayerExit() { /* nothing */ }
    
    public override void OnCombatStart()
    {
        isLocked = true;
        doorCollider.enabled = true;
        if (spriteRenderer != null)
            spriteRenderer.sprite = lockedSprite;
        Debug.Log($"[CombatDoor] Door locked at {gridPosition}");
    }
    
    public override void OnCombatEnd()
    {
        isLocked = false;
        doorCollider.enabled = false;
        if (spriteRenderer != null)
            spriteRenderer.sprite = unlockedSprite;
        Debug.Log($"[CombatDoor] Door unlocked at {gridPosition}");
    }
    
    public bool IsLocked => isLocked;
}

/// <summary>
/// Destructible wall that takes damage and can be destroyed.
/// When destroyed, the wall tile is removed and no longer blocks pathfinding.
/// </summary>
public class DestructibleWall : TileInteraction
{
    [Header("Health")]
    [SerializeField] private int maxHealth = 3;
    [SerializeField] private GameObject destructionEffectPrefab;
    
    [Header("Visuals")]
    [SerializeField] private Tile damagedTilePrefab;
    [SerializeField] private Tile destroyedTilePrefab;
    
    private int currentHealth;
    private bool isDestroyed = false;
    
    private void Awake()
    {
        currentHealth = maxHealth;
    }
    
    public override void OnPlayerEnter() { /* nothing */ }
    public override void OnPlayerExit() { /* nothing */ }
    public override void OnCombatStart() { /* nothing */ }
    public override void OnCombatEnd() { /* nothing */ }
    
    /// <summary>Take damage. If health reaches 0, destroy the wall.</summary>
    public void TakeDamage(int amount)
    {
        if (isDestroyed) return;
        
        currentHealth -= amount;
        Debug.Log($"[DestructibleWall] Took {amount} damage. Health: {currentHealth}/{maxHealth}");
        
        if (currentHealth <= 0)
        {
            Destroy();
        }
        else
        {
            UpdateVisuals();
        }
    }
    
    private void Destroy()
    {
        isDestroyed = true;
        
        // Remove from tilemap (no longer blocks pathfinding)
        if (wallsTilemap != null)
            wallsTilemap.SetTile(ToVector3Int(gridPosition), null);
        
        // Visual feedback
        if (destructionEffectPrefab != null)
            Instantiate(destructionEffectPrefab, transform.position, Quaternion.identity);
        
        // Destroy this GameObject
        Destroy(gameObject);
        
        Debug.Log($"[DestructibleWall] Wall destroyed at {gridPosition}");
    }
    
    private void UpdateVisuals()
    {
        // Optional: Update the tile to show damage
        if (damagedTilePrefab != null && wallsTilemap != null)
        {
            wallsTilemap.SetTile(ToVector3Int(gridPosition), damagedTilePrefab);
        }
    }
    
    public int GetCurrentHealth() => currentHealth;
    public bool IsDestroyed => isDestroyed;
}

/// <summary>
/// Pressure plate that triggers effects when player enters.
/// Can open doors, heal player, reveal secrets, etc.
/// </summary>
public class PressurePlate : TileInteraction
{
    [Header("Trigger Effect")]
    [SerializeField] private GameObject triggerEffectPrefab;
    [SerializeField] private float activationCooldown = 1f;
    [SerializeField] private bool repeatableActivation = true;
    
    private bool canActivate = true;
    
    public override void OnPlayerEnter()
    {
        if (!canActivate) return;
        
        Activate();
        
        if (!repeatableActivation)
            canActivate = false;
        else
            StartCoroutine(ActivationCooldownRoutine());
    }
    
    public override void OnPlayerExit() { /* nothing */ }
    public override void OnCombatStart() { /* nothing */ }
    public override void OnCombatEnd() { /* nothing */ }
    
    private void Activate()
    {
        // Play effect
        if (triggerEffectPrefab != null)
            Instantiate(triggerEffectPrefab, transform.position, Quaternion.identity);
        
        Debug.Log($"[PressurePlate] Activated at {gridPosition}");
        
        // Subclasses can override to add custom behavior
        OnActivate();
    }
    
    /// <summary>Override this in subclasses to add custom behavior.</summary>
    protected virtual void OnActivate()
    {
        // Example behaviors (uncomment to use):
        
        // Heal player:
        // Unit player = FindFirstObjectByType<Unit>();
        // if (player != null)
        //     player.GetComponent<HealthComponent>().Heal(5);
        
        // Open nearby doors:
        // OpenDoorsNearby(5);  // 5 tiles radius
        
        // Reveal secret room:
        // RevealSecretRoom();
    }
    
    private System.Collections.IEnumerator ActivationCooldownRoutine()
    {
        canActivate = false;
        yield return new WaitForSeconds(activationCooldown);
        canActivate = true;
    }
}

/// <summary>
/// Trap tile that damages the player when they step on it.
/// </summary>
public class TrapTile : TileInteraction
{
    [Header("Trap Settings")]
    [SerializeField] private int damageAmount = 5;
    [SerializeField] private GameObject trapEffectPrefab;
    [SerializeField] private float triggerCooldown = 2f;
    
    private bool canTrigger = true;
    
    public override void OnPlayerEnter()
    {
        if (!canTrigger) return;
        
        // ✅ FIXED: Use FindFirstObjectByType instead of FindObjectOfType
        Unit player = FindFirstObjectByType<Unit>();
        if (player != null)
        {
            HealthComponent health = player.GetComponent<HealthComponent>();
            if (health != null)
            {
                health.TakeDamage(damageAmount);
                Debug.Log($"[TrapTile] Player hit trap! Took {damageAmount} damage");
            }
        }
        
        // Visual feedback
        if (trapEffectPrefab != null)
            Instantiate(trapEffectPrefab, transform.position, Quaternion.identity);
        
        canTrigger = false;
        StartCoroutine(TriggerCooldownRoutine());
    }
    
    public override void OnPlayerExit() { /* nothing */ }
    public override void OnCombatStart() { /* nothing */ }
    public override void OnCombatEnd() { /* nothing */ }
    
    private System.Collections.IEnumerator TriggerCooldownRoutine()
    {
        yield return new WaitForSeconds(triggerCooldown);
        canTrigger = true;
    }
}

/// <summary>
/// Healing fountain that heals the player when they enter.
/// </summary>
public class HealingFountain : TileInteraction
{
    [Header("Healing")]
    [SerializeField] private int healAmount = 10;
    [SerializeField] private GameObject healEffectPrefab;
    [SerializeField] private float healCooldown = 5f;
    
    private bool canHeal = true;
    
    public override void OnPlayerEnter()
    {
        if (!canHeal) return;
        
        // ✅ FIXED: Use FindFirstObjectByType instead of FindObjectOfType
        Unit player = FindFirstObjectByType<Unit>();
        if (player != null)
        {
            HealthComponent health = player.GetComponent<HealthComponent>();
            if (health != null)
            {
                health.Heal(healAmount);
                Debug.Log($"[HealingFountain] Player healed for {healAmount}!");
            }
        }
        
        // Visual feedback
        if (healEffectPrefab != null)
            Instantiate(healEffectPrefab, transform.position, Quaternion.identity);
        
        canHeal = false;
        StartCoroutine(HealCooldownRoutine());
    }
    
    public override void OnPlayerExit() { /* nothing */ }
    public override void OnCombatStart() { /* nothing */ }
    public override void OnCombatEnd() { /* nothing */ }
    
    private System.Collections.IEnumerator HealCooldownRoutine()
    {
        yield return new WaitForSeconds(healCooldown);
        canHeal = true;
    }
}