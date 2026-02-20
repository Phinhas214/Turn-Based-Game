using System;
using UnityEngine;

public class SpinAction : BaseAction
{
    private float totalSpinAmount;

    private void Update()
    {
        if (!isActive)
        {
            return;
        }

        float spinAddAmount = 360 * Time.deltaTime;
        transform.eulerAngles += new Vector3(0, spinAddAmount, 0);   

        totalSpinAmount += spinAddAmount;

        if (totalSpinAmount > 360)
        {
            isActive = false;
            onActionComplete();
        }
    }


    public void Spin(Action onActionComplete)
    {
        this.onActionComplete = onActionComplete;
        isActive = true;
        totalSpinAmount = 0f;

        // spin action takes 2 stamina points. 
        int current = playerStats.GetCurrentStaminaPoints();
        playerStats.SetCurrentStaminaPoints(current - 1);

    }

    public override string GetActionName()
    {
        return "Spin";
    }
}