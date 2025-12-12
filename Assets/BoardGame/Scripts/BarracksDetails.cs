using UnityEngine;

[CreateAssetMenu(fileName = "BarracksDetails", menuName = "Units/Barracks Details")]
public class BarracksDetails : ScriptableObject, IUnitDetails
{
    [SerializeField] private string unitName = "Barracks";
    [SerializeField] private GameObject modelPrefab;
    [SerializeField] private Vector3 modelPositionOffset = new Vector3(1.203f, 0.342f, -1.189f);
    [SerializeField] private Vector3 modelRotationEuler = new Vector3(-90f, 0f, 0f);
    [SerializeField] private float modelHeight = 0.36f;
    [SerializeField] private Vector2Int footprintSize = new Vector2Int(2, 2);

    public string UnitName => unitName;
    public GameObject ModelPrefab => modelPrefab;
    public Vector3 ModelPositionOffset => modelPositionOffset;
    public Quaternion ModelRotation => Quaternion.Euler(modelRotationEuler);
    public float ModelHeight => modelHeight;
    public Vector2Int FootprintSize => footprintSize;

    public Vector3[] GetSquadFormation()
    {
        // Single building at center
        return new Vector3[] { Vector3.zero };
    }
}
