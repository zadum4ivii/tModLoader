using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using Terraria;

namespace Terraria.ModLoader
{
	public class ModMountData
	{
		internal string texture;

		public Mount.MountData mountData
		{
			get;
			internal set;
		}

		public Mod mod
		{
			get;
			internal set;
		}

		public int Type
		{
			get;
			internal set;
		}

		public string Name
		{
			get;
			internal set;
		}

		public ModMountData()
		{
			mountData = new Mount.MountData();
		}

		public virtual bool Autoload(ref string name, ref string texture, IDictionary<MountTextureType, string> extraTextures)
		{
			return mod.Properties.Autoload;
		}
		public int[] standingFrame { get; set; }
		public bool idleLoop { get; set; }
		public int[] dashingFrame { get; set; }
		public int[] swimmingFrame { get; set; }
		public int[] idleFrame { get; set; }
		public int[] flyingFrame { get; set; }
		public int[] runningFrame { get; set; }
		public int[] inAirFrame { get; set; }
		internal void SetupMount(Mount.MountData mountData)
		{
			ModMountData newMountData = (ModMountData)MemberwiseClone();
			newMountData.mountData = mountData;
			mountData.modMountData = newMountData;
			newMountData.mod = mod;
			newMountData.SetDefaults();
			mountData.runningFrameStart = runningFrame[0];
			mountData.runningFrameCount = runningFrame[1];
			mountData.runningFrameDelay = runningFrame[2];

			mountData.flyingFrameStart = flyingFrame[0];
			mountData.flyingFrameCount = flyingFrame[1];
			mountData.flyingFrameDelay = flyingFrame[2];

			mountData.standingFrameStart = standingFrame[0];
			mountData.standingFrameCount = standingFrame[1];
			mountData.standingFrameDelay = standingFrame[2];

			mountData.swimFrameStart = swimmingFrame[0];
			mountData.swimFrameCount = swimmingFrame[1];
			mountData.swimFrameDelay = swimmingFrame[2];

			mountData.dashingFrameStart = dashingFrame[0];
			mountData.dashingFrameCount = dashingFrame[1];
			mountData.dashingFrameDelay = dashingFrame[2];

			mountData.inAirFrameStart = inAirFrame[0];
			mountData.inAirFrameCount = inAirFrame[1];
			mountData.inAirFrameDelay = inAirFrame[2];

			mountData.idleFrameStart = idleFrame[0];
			mountData.idleFrameCount = idleFrame[1];
			mountData.idleFrameDelay = idleFrame[2];
			mountData.idleFrameLoop = idleLoop;
		}

		public virtual void SetDefaults()
		{
		}

		public virtual void UpdateEffects(Player player)
		{
		}

		public virtual bool UpdateFrame(Player mountedPlayer, int state, Vector2 velocity)
		{
			return true;
		}

		public virtual bool CustomBodyFrame()
		{
			return false;
		}
		public virtual void UseAbility(Player player, Vector2 mousePosition, bool toggleOn)
		{

		}
		public virtual void AimAbility(Player player, Vector2 mousePosition)
		{

		}
	}
}
