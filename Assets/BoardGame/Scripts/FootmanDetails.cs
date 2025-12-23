using UnityEngine;

[CreateAssetMenu(fileName = "FootmanDetails", menuName = "Units/Footman Details")]
public class FootmanDetails : BaseUnitDetails
{
    public override Vector3[] GetSquadFormation()
    {
        // 9 footmen: 4 in front row, 5 in back row, spread across 3 tiles
        Vector3[] positions = new Vector3[9];
        int index = 0;
        float unitSpacing = 4.0f / 7.0f;

        // Back row: 5 footmen spanning full width (0 to 2)
        for (int i = 0; i < 5; i++)
        {
            float x = i * unitSpacing; // Same spacing as before
            positions[index++] = new Vector3(x, 0, 0.5f); // Back row
        }

        // Front row: 4 footmen more inward, narrower spread
        float frontRowOffset = unitSpacing / 2.0f; // Offset to center and inset
        for (int i = 0; i < 4; i++)
        {
            float x = frontRowOffset + (i * unitSpacing);
            positions[index++] = new Vector3(x, 0, 0); // Front row
        }

        return positions;
    }

}
