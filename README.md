TODO: 
    - After implementing the turn system. Coordinate with Sam to update the action points and the corresponding UI for action points. 
        - After every turn unit action points should reset to max stamina. 
    - Timer for each turn
        - there's a 30 second timer for each turn before the turn ends and moves on to the next one. 
        - for this you've already implemented NextTurn() in TurnSystem so you can call that and the functions subscribed to this 
            event will trigger on thier own update. 
Notes: 
    I'm considering both units as player units for now and will revisit turn system when we implement enemies. But for now turn system
    will update the player stats and turn system UI.   
