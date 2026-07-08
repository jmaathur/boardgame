using UnityEngine;

[CreateAssetMenu(fileName = "HolyKnightDetails", menuName = "Units/Holy Knight Details")]
public class HolyKnightDetails : BaseUnitDetails
{
    public override Vector3[] GetSquadFormation()
    {
        // Single unit at center
        return new Vector3[] { Vector3.zero };
    }
}
