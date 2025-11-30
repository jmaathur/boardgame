using UnityEngine;

public interface IUnitDetails
{
    string UnitName { get; }
    GameObject ModelPrefab { get; }
    Vector3 ModelPositionOffset { get; }
    Quaternion ModelRotation { get; }
    float ModelHeight { get; }
    Vector2Int FootprintSize { get; }
}
