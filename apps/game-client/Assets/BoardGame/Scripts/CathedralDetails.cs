using UnityEngine;

[CreateAssetMenu(fileName = "CathedralDetails", menuName = "Units/Cathedral Details")]
public class CathedralDetails : BaseUnitDetails
{
    public override Vector3[] GetSquadFormation()
    {
        // Single building at center
        return new Vector3[] { Vector3.zero };
    }
}
