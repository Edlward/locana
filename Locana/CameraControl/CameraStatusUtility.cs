﻿using Kazyx.RemoteApi.Camera;

namespace Locana.CameraControl
{
    public class CameraStatusUtility
    {
        internal static bool IsContinuousShootingMode(TargetDevice target)
        {
            return target?.Status?.ShootMode?.Current == ShootModeParam.Still &&
                (target.Status.ContShootingMode?.Current == ContinuousShootMode.Cont ||
                target.Status.ContShootingMode?.Current == ContinuousShootMode.SpeedPriority);
        }
    }
}
