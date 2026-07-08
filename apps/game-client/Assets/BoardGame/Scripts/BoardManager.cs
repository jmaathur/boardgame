using UnityEngine;
using System.Collections.Generic;

public class BoardManager : MonoBehaviour
{
    public Transform ParentTransform;
    public GameObject BoardTilePrefab;

    [Header("Unit Details")]
    public CathedralDetails cathedralDetails;
    public BarracksDetails barracksDetails;
    public HolyKnightDetails holyKnightDetails;
    public FootmanDetails footmanDetails;
    public ArcherDetails archerDetails;
    public WhelpDetails whelpDetails;

    private const int BOARD_WIDTH = 32;
    private const int BOARD_HEIGHT = 32;

    private GameObject[,] gameTiles;
    private Unit[,] tileOccupancy;
    private List<Unit> player1Units;
    private List<Unit> player2Units;
    private bool isHolyKnightPlacementMode = false;
    private bool isFootmanPlacementMode = false;
    private bool isArcherPlacementMode = false;
    private bool isWhelpPlacementMode = false;

    void Start()
    {
        InitializeBoard();
    }

    void InitializeBoard()
    {
        gameTiles = new GameObject[BOARD_WIDTH, BOARD_HEIGHT];
        tileOccupancy = new Unit[BOARD_WIDTH, BOARD_HEIGHT];
        player1Units = new List<Unit>();
        player2Units = new List<Unit>();

        for (int x = 0; x < BOARD_WIDTH; x++)
        {
            for (int y = 0; y < BOARD_HEIGHT; y++)
            {
                Vector3 position = new Vector3(x, 0, y);
                GameObject tile = Instantiate(BoardTilePrefab, position, Quaternion.identity);
                tile.name = $"Tile_{x}_{y}";
                if (ParentTransform != null)
                {
                    tile.transform.SetParent(ParentTransform, false);
                }
                string captionText = string.Format("B1:[{0:00},{1:00}]", x, y);

                #region Board Tile
                BoardTile tileUI = tile.GetComponentInChildren<BoardTile>();
                tileUI.boardTileCaptionText.text = captionText;
                tileUI.col = y;
                tileUI.row = x;
                #endregion

                gameTiles[x, y] = tile;
            }
        }

        // Place units on the board
        PlaceUnit(cathedralDetails, Player.Player1, 8, 5);
        PlaceUnit(barracksDetails, Player.Player1, 22, 5);
        PlaceUnit(barracksDetails, Player.Player2, 8, 26);
        PlaceUnit(cathedralDetails, Player.Player2, 22, 26);
    }

    Unit PlaceUnit(IUnitDetails unitDetails, Player owner, int startX, int startY)
    {
        if (unitDetails == null)
        {
            Debug.LogWarning($"Unit details is null, cannot place at ({startX}, {startY})");
            return null;
        }

        Vector2Int footprint = unitDetails.FootprintSize;

        // Check bounds
        if (startX < 0 || startY < 0 ||
            startX + footprint.x > BOARD_WIDTH ||
            startY + footprint.y > BOARD_HEIGHT)
        {
            Debug.LogError($"Unit placement out of bounds at ({startX}, {startY}) with size {footprint.x}x{footprint.y}");
            return null;
        }

        // Check if tiles are occupied
        if (!AreTilesAvailable(startX, startY, footprint))
        {
            Debug.LogError($"Tiles at ({startX}, {startY}) are already occupied");
            return null;
        }

        // Create Unit instance
        Unit unit = new Unit(owner, unitDetails, new Vector2Int(startX, startY));

        // Determine rotation (Player 2 units face opposite direction)
        Quaternion rotation = unitDetails.ModelRotation;
        if (owner == Player.Player2)
        {
            rotation = Quaternion.Euler(0, 180, 0) * rotation;
        }

        // Get squad formation and spawn models
        Vector3[] squadFormation = unitDetails.GetSquadFormation();
        GameObject squadParent = new GameObject($"{owner}_{unitDetails.UnitName}_{startX}_{startY}");
        squadParent.transform.SetParent(ParentTransform, true);

        for (int i = 0; i < squadFormation.Length; i++)
        {
            // Calculate world position for each model in the squad
            Vector3 basePosition = new Vector3(startX, unitDetails.ModelHeight, startY);
            Vector3 formationOffset = squadFormation[i];
            Vector3 worldPosition = basePosition + formationOffset + unitDetails.ModelPositionOffset;

            GameObject model = Instantiate(
                unitDetails.ModelPrefab,
                worldPosition,
                rotation
            );
            model.name = $"{unitDetails.UnitName}_{i}";
            model.transform.SetParent(squadParent.transform, true);
        }

        unit.VisualInstance = squadParent;

        // Mark tiles as occupied
        MarkTilesOccupied(unit, startX, startY, footprint);

        // Add to appropriate player's unit list
        if (owner == Player.Player1)
            player1Units.Add(unit);
        else
            player2Units.Add(unit);

        Debug.Log($"Placed {owner} {unitDetails.UnitName} at ({startX}, {startY})");
        return unit;
    }

    bool AreTilesAvailable(int startX, int startY, Vector2Int footprint)
    {
        for (int x = startX; x < startX + footprint.x; x++)
            for (int y = startY; y < startY + footprint.y; y++)
                if (tileOccupancy[x, y] != null)
                    return false;
        return true;
    }

    void MarkTilesOccupied(Unit unit, int startX, int startY, Vector2Int footprint)
    {
        for (int x = startX; x < startX + footprint.x; x++)
            for (int y = startY; y < startY + footprint.y; y++)
                tileOccupancy[x, y] = unit;
    }

    void ClearTileOccupancy(Unit unit)
    {
        Vector2Int footprint = unit.UnitDetails.FootprintSize;
        Vector2Int pos = unit.PlacementPosition;
        for (int x = pos.x; x < pos.x + footprint.x; x++)
            for (int y = pos.y; y < pos.y + footprint.y; y++)
                if (tileOccupancy[x, y] == unit)
                    tileOccupancy[x, y] = null;
    }

    public Unit GetUnitAtPosition(int x, int y)
    {
        if (x >= 0 && x < BOARD_WIDTH && y >= 0 && y < BOARD_HEIGHT)
            return tileOccupancy[x, y];
        return null;
    }

    public GameObject GetTile(int x, int y)
    {
        if (x >= 0 && x < BOARD_WIDTH && y >= 0 && y < BOARD_HEIGHT)
        {
            return gameTiles[x, y];
        }
        return null;
    }

    public void ToggleHolyKnightPlacementMode()
    {
        isHolyKnightPlacementMode = !isHolyKnightPlacementMode;
        Debug.Log($"Holy Knight placement mode: {(isHolyKnightPlacementMode ? "ON" : "OFF")}");
    }

    public void ToggleFootmanPlacementMode()
    {
        isFootmanPlacementMode = !isFootmanPlacementMode;
        Debug.Log($"Footman placement mode: {(isFootmanPlacementMode ? "ON" : "OFF")}");
    }

    public void ToggleArcherPlacementMode()
    {
        isArcherPlacementMode = !isArcherPlacementMode;
        Debug.Log($"Archer placement mode: {(isArcherPlacementMode ? "ON" : "OFF")}");
    }

    public void ToggleWhelpPlacementMode()
    {
        isWhelpPlacementMode = !isWhelpPlacementMode;
        Debug.Log($"Whelp placement mode: {(isWhelpPlacementMode ? "ON" : "OFF")}");
    }

    void Update()
    {
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);

        #region MOUSE CLICK
        if (Input.GetMouseButtonDown(0))
        {
            RaycastHit hit;
            if (Physics.Raycast(ray, out hit, 200))
            {
                if (hit.transform.tag.Equals("BoardTile"))
                {
                    var bt = hit.transform.GetComponent<BoardTile>();

                    if (isHolyKnightPlacementMode)
                    {
                        // Place Holy Knight at this tile
                        // Note: bt.row stores x coordinate, bt.col stores y coordinate
                        int tileX = bt.row;
                        int tileY = bt.col;

                        Debug.Log($"Attempting to place Holy Knight at tile ({tileX}, {tileY})");

                        // Check if tile is empty
                        if (GetUnitAtPosition(tileX, tileY) == null)
                        {
                            PlaceUnit(holyKnightDetails, Player.Player1, tileX, tileY);
                            isHolyKnightPlacementMode = false; // Exit placement mode after placing
                            Debug.Log($"Placed Holy Knight at ({tileX}, {tileY}). Placement mode OFF.");
                        }
                        else
                        {
                            Debug.LogWarning($"Cannot place Holy Knight at ({tileX}, {tileY}) - tile is occupied");
                        }
                    }
                    else if (isFootmanPlacementMode)
                    {
                        // Place Footman at this tile
                        // Note: bt.row stores x coordinate, bt.col stores y coordinate
                        int tileX = bt.row;
                        int tileY = bt.col;

                        Debug.Log($"Attempting to place Footman at tile ({tileX}, {tileY})");

                        // Check if tile is empty
                        if (GetUnitAtPosition(tileX, tileY) == null)
                        {
                            PlaceUnit(footmanDetails, Player.Player1, tileX, tileY);
                            isFootmanPlacementMode = false; // Exit placement mode after placing
                            Debug.Log($"Placed Footman at ({tileX}, {tileY}). Placement mode OFF.");
                        }
                        else
                        {
                            Debug.LogWarning($"Cannot place Footman at ({tileX}, {tileY}) - tile is occupied");
                        }
                    }
                    else if (isArcherPlacementMode)
                    {
                        // Place Archer at this tile
                        int tileX = bt.row;
                        int tileY = bt.col;

                        Debug.Log($"Attempting to place Archer at tile ({tileX}, {tileY})");

                        if (GetUnitAtPosition(tileX, tileY) == null)
                        {
                            PlaceUnit(archerDetails, Player.Player1, tileX, tileY);
                            isArcherPlacementMode = false;
                            Debug.Log($"Placed Archer at ({tileX}, {tileY}). Placement mode OFF.");
                        }
                        else
                        {
                            Debug.LogWarning($"Cannot place Archer at ({tileX}, {tileY}) - tile is occupied");
                        }
                    }
                    else if (isWhelpPlacementMode)
                    {
                        // Place Whelp at this tile
                        int tileX = bt.row;
                        int tileY = bt.col;

                        Debug.Log($"Attempting to place Whelp at tile ({tileX}, {tileY})");

                        if (GetUnitAtPosition(tileX, tileY) == null)
                        {
                            PlaceUnit(whelpDetails, Player.Player1, tileX, tileY);
                            isWhelpPlacementMode = false;
                            Debug.Log($"Placed Whelp at ({tileX}, {tileY}). Placement mode OFF.");
                        }
                        else
                        {
                            Debug.LogWarning($"Cannot place Whelp at ({tileX}, {tileY}) - tile is occupied");
                        }
                    }
                    else
                    {
                        Debug.Log(bt.boardTileCaptionText.text);
                    }
                }
            }
        }
        #endregion MOUSE CLICK
    }
}
