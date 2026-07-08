using UnityEngine;

// Matches ArcherDetails.asset:
// - Footprint Size: (3, 1) - Archers occupy 3 tiles width, 1 tile depth
// - Model Prefab: ArcherInTheMist prefab
[CreateAssetMenu(fileName = "ArcherDetails", menuName = "Units/Archer Details")]
public class ArcherDetails : BaseUnitDetails
{
    public override Vector3[] GetSquadFormation()
    {
        // 7 archers: 3 in the front row, 4 staggered across the back row.
        Vector3[] positions = new Vector3[7];

        // Front row: 3 archers
        positions[0] = new Vector3(0, 0, 0);      // Front left
        positions[1] = new Vector3(0.5f, 0, 0);   // Front center
        positions[2] = new Vector3(1.0f, 0, 0);   // Front right

        // Back row: 4 archers (staggered)
        positions[3] = new Vector3(-0.25f, 0, 0.5f);  // Back far-left
        positions[4] = new Vector3(0.25f, 0, 0.5f);   // Back left
        positions[5] = new Vector3(0.75f, 0, 0.5f);   // Back right
        positions[6] = new Vector3(1.25f, 0, 0.5f);   // Back far-right

        return positions;
    }
}
