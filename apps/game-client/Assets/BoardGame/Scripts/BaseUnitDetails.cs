using UnityEngine;

public abstract class BaseUnitDetails : ScriptableObject, IUnitDetails
{
    [SerializeField] private string unitName;
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

    public abstract Vector3[] GetSquadFormation();
}
