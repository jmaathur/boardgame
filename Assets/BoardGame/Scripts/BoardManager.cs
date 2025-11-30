using UnityEngine;
using System.Collections.Generic;

public class BoardManager : MonoBehaviour
{
    public Transform ParentTransform;
    public GameObject BoardTilePrefab;

    [Header("Unit Details")]
    public CathedralDetails cathedralDetails;
    public BarracksDetails barracksDetails;

    private const int BOARD_WIDTH = 32;
    private const int BOARD_HEIGHT = 32;

    private GameObject[,] gameTiles;
    private Unit[,] tileOccupancy;
    private List<Unit> player1Units;
    private List<Unit> player2Units;

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
        PlaceUnit(cathedralDetails, Player.Player2, 8, 26);
        PlaceUnit(barracksDetails, Player.Player2, 22, 26);
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

        // Calculate world position
        float centerX = startX + (footprint.x - 1) * 0.5f;
        float centerY = startY + (footprint.y - 1) * 0.5f;
        Vector3 position = new Vector3(centerX, unitDetails.ModelHeight, centerY);

        // Spawn visual model
        GameObject visual = Instantiate(
            unitDetails.ModelPrefab,
            position + unitDetails.ModelPositionOffset,
            unitDetails.ModelRotation
        );
        visual.name = $"{owner}_{unitDetails.UnitName}_{startX}_{startY}";
        visual.transform.SetParent(ParentTransform, true);
        unit.VisualInstance = visual;

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
                    Debug.Log(bt.boardTileCaptionText.text);
                }
            }
        }
        #endregion MOUSE CLICK
    }
}
