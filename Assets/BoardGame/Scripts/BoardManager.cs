using UnityEngine;

public class BoardManager : MonoBehaviour
{
    public Transform ParentTransform;
    public GameObject BoardTilePrefab;

    private const int BOARD_WIDTH = 72;
    private const int BOARD_HEIGHT = 60;

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
