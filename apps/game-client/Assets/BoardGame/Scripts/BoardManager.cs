using UnityEngine;
using System.Collections.Generic;
using BoardGame.Net;

/// <summary>
/// Renders a shared, server-authoritative board. The client never mutates the
/// board directly: a placement-mode click asks the server to place a unit, and
/// the board is redrawn from the state the server broadcasts back to every
/// client in the room. Two clients therefore can never disagree — both mirror
/// the same broadcast.
///
/// The tile grid is built from the board size the server reports
/// (<see cref="GameStateDto.board"/>), not a hardcoded constant, and every unit
/// is tinted by ownership (mine vs. theirs) so open, color-coded placement is
/// visible on both screens.
///
/// Editor wiring (see docs/two-client-shared-board-plan.md §3): drop a
/// GameServerClient onto a "Net" GameObject, drag it into <see cref="client"/>,
/// and assign the Mine/Theirs materials.
/// </summary>
public class BoardManager : MonoBehaviour
{
    public Transform ParentTransform;
    public GameObject BoardTilePrefab;

    [Header("Networking")]
    [Tooltip("The GameServerClient this board mirrors. Assign in the Editor (see plan §3).")]
    [SerializeField] private GameServerClient client;

    [Header("Ownership Tint")]
    [Tooltip("Material applied to units owned by this client. Optional — if unset, mineColor is used as a per-renderer tint.")]
    [SerializeField] private Material mineMaterial;
    [Tooltip("Material applied to units owned by other clients in the room. Optional — if unset, theirsColor is used as a per-renderer tint.")]
    [SerializeField] private Material theirsMaterial;
    [Tooltip("Fallback tint for my units when Mine Material is unassigned.")]
    [SerializeField] private Color mineColor = new Color(0.20f, 0.70f, 0.65f);   // teal
    [Tooltip("Fallback tint for opponent units when Theirs Material is unassigned.")]
    [SerializeField] private Color theirsColor = new Color(0.80f, 0.35f, 0.20f); // clay

    [Header("Unit Details")]
    // NOTE: these field names/types are referenced by GUID in SampleScene.unity.
    // Do not rename them or the scene's serialized asset references will break.
    public CathedralDetails cathedralDetails;
    public BarracksDetails barracksDetails;
    public HolyKnightDetails holyKnightDetails;
    public FootmanDetails footmanDetails;
    public ArcherDetails archerDetails;
    public WhelpDetails whelpDetails;

    // Catalog unit-type id -> visual details. Built in Awake from the serialized
    // *Details fields. Keys must match the server catalog ids (core/catalog).
    private Dictionary<string, IUnitDetails> detailsByType;

    // Units currently drawn, keyed by the SERVER's unit id, so a fresh broadcast
    // can be diffed against what is on screen (spawn new, move/retype = respawn,
    // despawn removed).
    private readonly Dictionary<string, RenderedUnit> rendered = new Dictionary<string, RenderedUnit>();

    private GameObject[,] gameTiles;
    private int boardW = GameProtocol.BoardWidth;   // pre-connect fallback; overwritten from welcome/state
    private int boardH = GameProtocol.BoardHeight;
    private bool gridBuilt = false;

    private string myPlayerId;
    // The unit type the next tile click will request, or null when just
    // inspecting. Replaces the old per-type placement-mode booleans.
    private string selectedUnitType = null;

    /// <summary>A drawn unit: its root GameObject plus the last state we drew it from.</summary>
    private class RenderedUnit
    {
        public GameObject root;
        public UnitDto last;
    }

    void Awake()
    {
        detailsByType = new Dictionary<string, IUnitDetails>
        {
            [GameProtocol.UnitFootman] = footmanDetails,
            [GameProtocol.UnitArcher] = archerDetails,
            [GameProtocol.UnitWhelp] = whelpDetails,
            [GameProtocol.UnitHolyKnight] = holyKnightDetails,
            [GameProtocol.UnitBarracks] = barracksDetails,
            [GameProtocol.UnitCathedral] = cathedralDetails,
        };

        if (client == null)
        {
            Debug.LogError("[BoardManager] Client is not assigned. Drag the Net GameObject " +
                "(GameServerClient) into BoardManager's Client field — see plan §3. " +
                "The board will stay empty until it is wired.");
        }
    }

    void OnEnable()
    {
        if (client == null) return;
        client.WelcomeReceived += OnWelcome;
        client.StateReceived += OnState;
        client.ErrorReceived += OnError;
    }

    void OnDisable()
    {
        if (client == null) return;
        client.WelcomeReceived -= OnWelcome;
        client.StateReceived -= OnState;
        client.ErrorReceived -= OnError;
    }

    // ------------------------------------------------------------------
    // Server events (all raised on the main thread by GameServerClient)
    // ------------------------------------------------------------------

    private void OnWelcome(WelcomeMessage w)
    {
        myPlayerId = w.playerId;
        // Welcome carries the full state; RenderState (re)builds the grid and
        // draws every unit already in the room, so late joiners are correct.
        RenderState(w.state);
    }

    private void OnState(GameStateDto s)
    {
        RenderState(s);
    }

    private void OnError(ErrorMessage e)
    {
        // A rejected placement produces no state change, so nothing is drawn —
        // exactly right. Surface the reason for the player who acted.
        Debug.LogWarning($"[BoardManager] Placement rejected — {e.code}: {e.message}");
    }

    // ------------------------------------------------------------------
    // Rendering
    // ------------------------------------------------------------------

    /// <summary>
    /// Reconcile the drawn board with an authoritative snapshot. Diffs by unit
    /// id: unchanged units are left alone, moved/retyped units are respawned,
    /// and units the server no longer reports are removed.
    /// </summary>
    private void RenderState(GameStateDto s)
    {
        if (s == null) return;

        if (s.board != null)
        {
            EnsureGrid(s.board.width, s.board.height);
        }
        else if (!gridBuilt)
        {
            EnsureGrid(boardW, boardH);
        }

        var seen = new HashSet<string>();
        if (s.units != null)
        {
            foreach (var unit in s.units)
            {
                if (unit == null || string.IsNullOrEmpty(unit.id)) continue;
                seen.Add(unit.id);

                if (rendered.TryGetValue(unit.id, out var existing))
                {
                    if (Changed(existing.last, unit))
                    {
                        // Simplest correct reconciliation: destroy and respawn
                        // at the new spot / type / owner.
                        Destroy(existing.root);
                        rendered[unit.id] = SpawnUnit(unit);
                    }
                    // else unchanged — leave it in place.
                }
                else
                {
                    rendered[unit.id] = SpawnUnit(unit);
                }
            }
        }

        // Remove anything the server no longer reports (sold, or owner left).
        if (rendered.Count != seen.Count)
        {
            var stale = new List<string>();
            foreach (var id in rendered.Keys)
            {
                if (!seen.Contains(id)) stale.Add(id);
            }
            foreach (var id in stale)
            {
                Destroy(rendered[id].root);
                rendered.Remove(id);
            }
        }
    }

    private static bool Changed(UnitDto a, UnitDto b)
    {
        return a.row != b.row || a.col != b.col ||
               a.unitType != b.unitType || a.ownerId != b.ownerId;
    }

    /// <summary>
    /// Spawn the visual for one server unit and return its handle. Known unit
    /// types use their squad formation; unknown types fall back to a tinted
    /// footprint-sized placeholder so a server-added type never crashes us.
    /// </summary>
    private RenderedUnit SpawnUnit(UnitDto u)
    {
        bool mine = (u.ownerId == myPlayerId);

        var squadParent = new GameObject($"{u.ownerId}_{u.unitType}_{u.id}");
        // false: keep the parent's transform; we position models in world space.
        if (ParentTransform != null)
        {
            squadParent.transform.SetParent(ParentTransform, false);
        }

        IUnitDetails details = null;
        if (detailsByType != null)
        {
            detailsByType.TryGetValue(u.unitType, out details);
        }

        if (details != null && details.ModelPrefab != null)
        {
            SpawnSquad(u, details, mine, squadParent.transform);
        }
        else
        {
            // Unknown type, or a known type whose prefab isn't assigned.
            SpawnPlaceholder(u, details, mine, squadParent.transform);
        }

        return new RenderedUnit { root = squadParent, last = u };
    }

    private void SpawnSquad(UnitDto u, IUnitDetails details, bool mine, Transform parent)
    {
        // Opponent squads face back toward this client (replaces the old
        // Player.Player2 branch — keyed on ownership, not a hardcoded enum).
        Quaternion rotation = details.ModelRotation;
        if (!mine)
        {
            rotation = Quaternion.Euler(0, 180, 0) * rotation;
        }

        Vector3 basePosition = new Vector3(u.row, details.ModelHeight, u.col);
        Vector3[] squadFormation = details.GetSquadFormation();

        for (int i = 0; i < squadFormation.Length; i++)
        {
            Vector3 worldPosition = basePosition + squadFormation[i] + details.ModelPositionOffset;
            GameObject model = Instantiate(details.ModelPrefab, worldPosition, rotation);
            model.name = $"{details.UnitName}_{i}";
            model.transform.SetParent(parent, true);
            ApplyTint(model, mine);
        }
    }

    /// <summary>
    /// A tinted cube sized to the unit's footprint, used when we cannot resolve
    /// a model prefab for a unit type. Never throws.
    /// </summary>
    private void SpawnPlaceholder(UnitDto u, IUnitDetails details, bool mine, Transform parent)
    {
        Vector2Int footprint = details != null ? details.FootprintSize : new Vector2Int(1, 1);
        if (footprint.x < 1) footprint.x = 1;
        if (footprint.y < 1) footprint.y = 1;

        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = $"placeholder_{u.unitType}";
        // CreatePrimitive attaches a BoxCollider; drop it so the placeholder
        // never intercepts the placement raycast (which looks for BoardTile).
        var collider = cube.GetComponent<Collider>();
        if (collider != null) Destroy(collider);
        // Cube spans the footprint (Unity cube is 1x1x1); centre it over the
        // footprint's tiles with a modest height.
        cube.transform.localScale = new Vector3(footprint.x, 0.5f, footprint.y);
        cube.transform.position = new Vector3(
            u.row + (footprint.x - 1) * 0.5f,
            0.25f,
            u.col + (footprint.y - 1) * 0.5f);
        cube.transform.SetParent(parent, true);
        ApplyTint(cube, mine);
    }

    /// <summary>
    /// Colour a spawned object by ownership. Prefers the assigned Mine/Theirs
    /// material; when that slot is unassigned, falls back to a per-renderer
    /// colour (via MaterialPropertyBlock) so ownership is still visible during
    /// guided setup. The property block only overrides the base colour and never
    /// clones or mutates a shared material asset.
    /// </summary>
    private void ApplyTint(GameObject go, bool mine)
    {
        var renderers = go.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) return;

        Material tint = mine ? mineMaterial : theirsMaterial;
        if (tint != null)
        {
            foreach (var r in renderers)
            {
                r.sharedMaterial = tint;
            }
            return;
        }

        // Fallback: no material wired yet — tint the base colour so mine vs
        // theirs is still distinguishable. URP Lit uses "_BaseColor"; the
        // Standard/legacy shader uses "_Color". Set both so either pipeline
        // shows the tint.
        Color color = mine ? mineColor : theirsColor;
        var block = new MaterialPropertyBlock();
        foreach (var r in renderers)
        {
            r.GetPropertyBlock(block);
            block.SetColor("_BaseColor", color);
            block.SetColor("_Color", color);
            r.SetPropertyBlock(block);
        }
    }

    // ------------------------------------------------------------------
    // Grid
    // ------------------------------------------------------------------

    /// <summary>
    /// Build (or rebuild) the tile grid to the given dimensions. No-op if the
    /// grid already matches. Rebuilding clears any drawn units — the next
    /// RenderState call redraws them from the authoritative snapshot.
    /// </summary>
    private void EnsureGrid(int width, int height)
    {
        if (width <= 0 || height <= 0) return;
        if (gridBuilt && width == boardW && height == boardH) return;

        if (gridBuilt)
        {
            ClearGrid();
        }

        boardW = width;
        boardH = height;
        gameTiles = new GameObject[boardW, boardH];

        for (int x = 0; x < boardW; x++)
        {
            for (int y = 0; y < boardH; y++)
            {
                Vector3 position = new Vector3(x, 0, y);
                GameObject tile = Instantiate(BoardTilePrefab, position, Quaternion.identity);
                tile.name = $"Tile_{x}_{y}";
                if (ParentTransform != null)
                {
                    tile.transform.SetParent(ParentTransform, false);
                }

                string captionText = string.Format("B1:[{0:00},{1:00}]", x, y);
                BoardTile tileUI = tile.GetComponentInChildren<BoardTile>();
                if (tileUI != null)
                {
                    tileUI.boardTileCaptionText.text = captionText;
                    tileUI.col = y;
                    tileUI.row = x;
                }

                gameTiles[x, y] = tile;
            }
        }

        gridBuilt = true;
    }

    private void ClearGrid()
    {
        if (gameTiles == null) return;
        for (int x = 0; x < gameTiles.GetLength(0); x++)
        {
            for (int y = 0; y < gameTiles.GetLength(1); y++)
            {
                if (gameTiles[x, y] != null) Destroy(gameTiles[x, y]);
            }
        }

        // Drawn units are children of the grid's world; drop their handles so
        // RenderState redraws them fresh against the rebuilt grid.
        foreach (var ru in rendered.Values)
        {
            if (ru.root != null) Destroy(ru.root);
        }
        rendered.Clear();
        gameTiles = null;
    }

    public GameObject GetTile(int x, int y)
    {
        if (gameTiles != null && x >= 0 && x < boardW && y >= 0 && y < boardH)
        {
            return gameTiles[x, y];
        }
        return null;
    }

    // ------------------------------------------------------------------
    // Placement input — scene buttons call these Toggle* methods by name.
    // Signatures must not change or SampleScene.unity's button wiring breaks.
    // ------------------------------------------------------------------

    public void ToggleHolyKnightPlacementMode() => SetSelected(GameProtocol.UnitHolyKnight);
    public void ToggleFootmanPlacementMode() => SetSelected(GameProtocol.UnitFootman);
    public void ToggleArcherPlacementMode() => SetSelected(GameProtocol.UnitArcher);
    public void ToggleWhelpPlacementMode() => SetSelected(GameProtocol.UnitWhelp);

    /// <summary>Select a unit type, or toggle it off if it was already selected.</summary>
    private void SetSelected(string unitType)
    {
        selectedUnitType = (selectedUnitType == unitType) ? null : unitType;
        Debug.Log(selectedUnitType == null
            ? "[BoardManager] Placement mode OFF."
            : $"[BoardManager] Placement mode: {selectedUnitType}");
    }

    void Update()
    {
        if (!Input.GetMouseButtonDown(0)) return;

        Camera cam = Camera.main;
        if (cam == null) return;

        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        if (!Physics.Raycast(ray, out RaycastHit hit, 200f)) return;
        if (!hit.transform.CompareTag("BoardTile")) return;

        var bt = hit.transform.GetComponent<BoardTile>();
        if (bt == null) return;

        if (selectedUnitType == null)
        {
            // Just inspecting.
            if (bt.boardTileCaptionText != null) Debug.Log(bt.boardTileCaptionText.text);
            return;
        }

        if (client == null)
        {
            Debug.LogWarning("[BoardManager] No client assigned; cannot place. See plan §3.");
            return;
        }

        // bt.row is the world-X tile index, bt.col the world-Z index — the same
        // convention the server uses. Ask the server; do NOT spawn locally. The
        // unit appears only when the server's state broadcast comes back.
        client.SendPlaceUnit(selectedUnitType, bt.row, bt.col);
        selectedUnitType = null; // one placement per selection
    }
}
