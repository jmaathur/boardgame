using UnityEngine;

public class BoardManager : MonoBehaviour
{
    public Transform ParentTransform;
    public GameObject BoardTilePrefab;

    [Header("Models")]
    public GameObject desertCathedralPrefab;
    public GameObject desertBarracksPrefab;

    [Header("Model Adjustments")]
    public Vector3 cathedralPositionOffset = Vector3.zero;
    public Vector3 barracksPositionOffset = Vector3.zero;

    private const int BOARD_WIDTH = 32;
    private const int BOARD_HEIGHT = 32;

    private GameObject[,] gameTiles;

    void Start()
    {
        InitializeBoard();
    }

    void InitializeBoard()
    {
        gameTiles = new GameObject[BOARD_WIDTH, BOARD_HEIGHT];

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

        // Place models on the board
        PlaceModel(desertCathedralPrefab, 8, 5, 2, 0.87f, cathedralPositionOffset);
        PlaceModel(desertBarracksPrefab, 22, 5, 2, 0.36f, barracksPositionOffset);
    }

    void PlaceModel(GameObject modelPrefab, int startX, int startY, int width, float centerZ, Vector3 positionOffset)
    {
        if (modelPrefab == null)
        {
            Debug.LogWarning($"Model prefab is null, cannot place at ({startX}, {startY})");
            return;
        }

        // Check bounds
        if (startX < 0 || startY < 0 || startX + width > BOARD_WIDTH || startY + width > BOARD_HEIGHT)
        {
            Debug.LogError($"Model placement out of bounds at ({startX}, {startY}) with size ({width}, {width})");
            return;
        }

        // Calculate center position for the model
        // For a tile at position X, it occupies X to X+1 in world space
        // So for a 2x2 model
        // The center should be at (startX + 1, startY + 1) to span across the correct tiles
        float centerX = startX + (width - 1) * 0.5f;
        float centerY = startY + (width - 1) * 0.5f;
        Vector3 modelPosition = new Vector3(centerX, centerZ, centerY);

        // Instantiate the model with 90 degree rotation on X axis to face upward
        Quaternion rotation = Quaternion.Euler(-90, 0, 0);
        GameObject model = Instantiate(modelPrefab, modelPosition + positionOffset, rotation);
        model.name = $"{modelPrefab.name}_{startX}_{startY}";

        if (ParentTransform != null)
        {
            model.transform.SetParent(ParentTransform, true);
        }

        Debug.Log($"Placed {modelPrefab.name} at calculated position {modelPosition}, with offset {positionOffset}, actual position: {model.transform.position}");
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
