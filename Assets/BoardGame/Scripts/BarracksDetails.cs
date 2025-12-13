using UnityEngine;

[CreateAssetMenu(fileName = "BarracksDetails", menuName = "Units/Barracks Details")]
public class BarracksDetails : BaseUnitDetails
{
    public override Vector3[] GetSquadFormation()
    {
        // Single building at center
        return new Vector3[] { Vector3.zero };
    }
}
