using System;
    private readonly ICameraMovement move;

    #region Properties
    public float MoveSpeed { get; set; } = 1;
    {
        get { return _upperPitch; }

        set
        {
            _upperPitch = MakeAcute(value);
    }
    #endregion

    #region Methods
    {
        if (angle < 0) { return 0; }
        if (angle > 90) { return 90; }
        return angle;
    }
    public void MoveLongitudinal(float input)

    #endregion