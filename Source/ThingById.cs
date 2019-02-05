﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;
using System.Reflection.Emit;
using Verse;
using RimWorld;
using Harmony;
using UnityEngine;

namespace TDBug
{
	[HarmonyPatch(typeof(EditWindow_DebugInspector), nameof(EditWindow_DebugInspector.DoWindowContents))]
	public static class ThingById_GUI
	{
		public static float headerHeight = 30f;
		//public override void DoWindowContents(Rect inRect)
		public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			foreach (CodeInstruction i in instructions)
			{
				yield return i;
				if (i.opcode == OpCodes.Ldc_R4 && (float)i.operand == headerHeight)
				{
					//Add another 30 pixels before readout to make room for id input (can't put it right of title, parent class controls that)
					yield return new CodeInstruction(OpCodes.Ldc_R4, headerHeight);
					yield return new CodeInstruction(OpCodes.Add);
				}
			}
		}

		public static int idToFind;
		public static string idBuffer;
		public static List<Thing> foundThings = new List<Thing>();
		public static void Prefix(Rect inRect)
		{
			if (Current.Game == null) return;

			Rect idRect = new Rect(inRect.x, inRect.y + headerHeight, inRect.width, headerHeight);
			int prevId = idToFind;
			Widgets.TextFieldNumeric(idRect.LeftHalf(), ref idToFind, ref idBuffer);
			if(idToFind != prevId)
			{
				foundThings = new List<Thing>(FindThings());
			}

			if (!foundThings.NullOrEmpty())
			{
				TargetHighlighter.Highlight(foundThings[0]);
				if (Widgets.ButtonText(idRect.RightHalf(), "Go to"))
				{
					Thing thing = foundThings[0];
					Current.Game.CurrentMap = thing.MapHeld;
					Find.CameraDriver.JumpToCurrentMapLoc(thing.PositionHeld);
				}
			}
		}

		public static IEnumerable<Thing> FindThings()
		{
			foreach (Thing thing in FindParentThings())
			{
				if (thing.thingIDNumber == idToFind)
					yield return thing;
				if(thing is IThingHolder thingHolder)
				{
					List<IThingHolder> childHolders = new List<IThingHolder>();
					thingHolder.GetChildHolders(childHolders);
					foreach (IThingHolder childHolder in childHolders)
						if(childHolder.GetDirectlyHeldThings() is ThingOwner owner)
							foreach (Thing childThing in owner)
								if (childThing.thingIDNumber == idToFind)
									yield return childThing;
				}
			}
		}

		public static IEnumerable<Thing> FindParentThings()
		{
			foreach (Map map in Current.Game.Maps)
				foreach (Thing thing in map.listerThings.AllThings)
					yield return thing;
			foreach (Thing thing in Current.Game.World.worldPawns.AllPawnsAliveOrDead)
				yield return thing;
		}
	}

	[HarmonyPatch(typeof(EditWindow_DebugInspector), "CurrentDebugString")]
	public static class ThingById_Readout
	{
		public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			 FieldInfo writeStorytellerInfo = AccessTools.Field(typeof(DebugViewSettings), nameof(DebugViewSettings.writeStoryteller));
			

			foreach (CodeInstruction i in instructions)
			{
				//ldfld        bool Verse.EditWindow_DebugInspector::fullMode
				if (i.opcode == OpCodes.Ldsfld && i.operand == writeStorytellerInfo)
				{
					yield return new CodeInstruction(OpCodes.Ldloc_0) { labels = i.labels }; //local StringBuilder ; todo: find it better
					yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(ThingById_Readout), nameof(ReadoutFoundThings)));
					i.labels = null;
				}

				yield return i;
			}
		}

		public static void ReadoutFoundThings(StringBuilder sb)
		{
			foreach (Thing thing in ThingById_GUI.foundThings)
			{
				sb.AppendLine($"--- Thing By ID: {thing}");

				IThingHolder holder = thing.ParentHolder;
				while (holder != null)
				{
					if (holder is Thing owner)
					{
						sb.AppendLine($"--- Held by {owner}");
						break;
					}
					holder = holder.ParentHolder;
				}
				sb.AppendLine(Scribe.saver.DebugOutputFor(thing));
			}
		}
	}
}
