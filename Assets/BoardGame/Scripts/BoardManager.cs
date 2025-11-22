using UnityEngine;

public class BoardManager : MonoBehaviour
{
    public Transform ParentTransform;
    public GameObject BoardTilePrefab;

    private const int BOARD_WIDTH = 62;
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
}
