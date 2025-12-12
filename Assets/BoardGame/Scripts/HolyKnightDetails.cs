using UnityEngine;

[CreateAssetMenu(fileName = "HolyKnightDetails", menuName = "Units/Holy Knight Details")]
public class HolyKnightDetails : ScriptableObject, IUnitDetails
{
    [SerializeField] private string unitName = "Holy Knight";
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

    public Vector3[] GetSquadFormation()
    {
        // Single unit at center
        return new Vector3[] { Vector3.zero };
    }
}
