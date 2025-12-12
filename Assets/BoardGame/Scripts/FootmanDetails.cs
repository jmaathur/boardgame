using UnityEngine;

[CreateAssetMenu(fileName = "FootmanDetails", menuName = "Units/Footman Details")]
public class FootmanDetails : ScriptableObject, IUnitDetails
{
    [SerializeField] private string unitName = "Footman";
    [SerializeField] private GameObject modelPrefab;
    [SerializeField] private Vector3 modelPositionOffset = Vector3.zero;
    [SerializeField] private Vector3 modelRotationEuler = new Vector3(-90f, 0f, 0f);
    [SerializeField] private float modelHeight = 0.36f;
    [SerializeField] private Vector2Int footprintSize = new Vector2Int(5, 1);

    public string UnitName => unitName;
    public GameObject ModelPrefab => modelPrefab;
    public Vector3 ModelPositionOffset => modelPositionOffset;
    public Quaternion ModelRotation => Quaternion.Euler(modelRotationEuler);
    public float ModelHeight => modelHeight;
    public Vector2Int FootprintSize => footprintSize;

    public Vector3[] GetSquadFormation()
    {
        // 15 footmen: 7 in front row, 8 in back row, spread across 5 tiles
        Vector3[] positions = new Vector3[15];
        int index = 0;
        float unitSpacing = 4.0f / 7.0f;

        // Back row: 8 footmen spanning full width (0 to 4)
        for (int i = 0; i < 8; i++)
        {
            float x = i * unitSpacing; // Distribute 8 across full 5-tile width
            positions[index++] = new Vector3(x, 0, 0.4f); // Back row
        }

        // Front row: 7 footmen more inward, narrower spread
        float frontRowOffset = unitSpacing / 2.0f; // Offset to center and inset
        for (int i = 0; i < 7; i++)
        {
            float x = frontRowOffset + (i * unitSpacing);
            positions[index++] = new Vector3(x, 0, 0); // Front row
        }

        return positions;
    }
}
