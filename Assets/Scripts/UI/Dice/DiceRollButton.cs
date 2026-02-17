using UnityEngine;

public class DiceRollButton : MonoBehaviour
{
    public DiceBoxUI diceBox;
    public DieType dieType = DieType.D6;
    public int count = 1;

    public DiceSpinVisual spinVisual; // reference cube

    public void Roll()
    {
        diceBox.RollDice(dieType, count);

        if (spinVisual != null)
            spinVisual.Spin();
    }
}
