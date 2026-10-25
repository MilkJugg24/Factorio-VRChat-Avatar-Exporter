namespace FactorioAvatarExporter.Core
{
    /// <summary>
    /// The tool a <see cref="DragonPose"/> holds: model.py's <c>Pose.prop</c> tuple. Model space, tiles.
    /// </summary>
    public abstract class DragonProp
    {
        private protected DragonProp()
        {
        }

        public abstract PropKind Kind { get; }

        /// <summary>The prop as Core's renderer-facing <see cref="PropPose"/>.</summary>
        public abstract PropPose ToPropPose();
    }

    /// <summary>A gun held by <see cref="DragonModel.HoldGun"/>: <c>("gun", stock, muzzle, aim)</c>.</summary>
    public sealed class DragonGun : DragonProp
    {
        public DragonGun(Vec3 stock, Vec3 muzzle, Vec3 aim)
        {
            Stock = stock;
            Muzzle = muzzle;
            Aim = aim;
        }

        public override PropKind Kind => PropKind.Gun;

        /// <summary><c>stock</c>: the back end of the gun.</summary>
        public Vec3 Stock { get; }

        /// <summary><c>muzzle</c>: the front end, <c>stock + aim * 0.52</c>.</summary>
        public Vec3 Muzzle { get; }

        /// <summary><c>aim</c>: unit vector the gun points along.</summary>
        public Vec3 Aim { get; }

        /// <summary>
        /// Base = stock, Tip = muzzle, Up = the opposite of the "down" vector model.py's <c>add_prop</c>
        /// hangs the magazine along (perpendicular to the aim, on the upper side).
        /// </summary>
        public override PropPose ToPropPose()
        {
            Vec3 down = Vec3.Cross(Vec3.Cross(Aim, Vec3.UnitZ), Aim).Normalized;
            if (Vec3.Dot(down, Vec3.UnitZ) > 0)
            {
                down = -down;
            }
            return new PropPose(PropKind.Gun, Stock, Muzzle, -down);
        }
    }

    /// <summary>A pickaxe held by <see cref="DragonModel.HoldPick"/>: <c>("pick", base, top, d, e)</c>.</summary>
    public sealed class DragonPick : DragonProp
    {
        public DragonPick(Vec3 basePoint, Vec3 top, Vec3 handleDirection, Vec3 headDirection)
        {
            Base = basePoint;
            Top = top;
            HandleDirection = handleDirection;
            HeadDirection = headDirection;
        }

        public override PropKind Kind => PropKind.Pickaxe;

        /// <summary><c>base</c>: the bottom end of the handle.</summary>
        public Vec3 Base { get; }

        /// <summary><c>top</c>: the top end of the handle, where the head is.</summary>
        public Vec3 Top { get; }

        /// <summary><c>d</c>: unit vector from <see cref="Base"/> towards <see cref="Top"/>.</summary>
        public Vec3 HandleDirection { get; }

        /// <summary>
        /// <c>e</c>: unit vector perpendicular to the handle, along the head; the pick's point extends
        /// towards -e.
        /// </summary>
        public Vec3 HeadDirection { get; }

        /// <summary>Base = base, Tip = top, Up = e.</summary>
        public override PropPose ToPropPose() => new PropPose(PropKind.Pickaxe, Base, Top, HeadDirection);
    }
}
