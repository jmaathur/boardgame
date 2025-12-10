using UnityEngine;

[CreateAssetMenu(fileName = "FootmanDetails", menuName = "Units/Footman Details")]
public class FootmanDetails : ScriptableObject, IUnitDetails
{
    [SerializeField] private string unitName = "Footman";
    [SerializeField] private GameObject modelPrefab;
    [SerializeField] private Vector3 modelPositionOffset = Vector3.zero;
    [SerializeField] private Vector3 modelRotationEuler = new Vector3(-90f, 0f, 0f);
    [SerializeField] private float modelHeight = 0.36f;
    [SerializeField] private Vector2Int footprintSize = new Vector2Int(1, 1);

    public string UnitName => unitName;
    public GameObject ModelPrefab => modelPrefab;
    public Vector3 ModelPositionOffset => modelPositionOffset;
    public Quaternion ModelRotation => Quaternion.Euler(modelRotationEuler);
    public float ModelHeight => modelHeight;
    public Vector2Int FootprintSize => footprintSize;
}
