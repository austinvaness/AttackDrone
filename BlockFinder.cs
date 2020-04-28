using Sandbox.ModAPI.Ingame;
using System.Collections.Generic;
using System.Linq;

namespace IngameScript
{
    partial class Program
    {
        #region Block Finder
        T GetBlock<T> (string name = null) where T : class, IMyTerminalBlock
        {
            List<T> blocks = new List<T>();
            if (string.IsNullOrEmpty(name))
                GridTerminalSystem.GetBlocksOfType(blocks, b => b.CubeGrid.EntityId == Me.CubeGrid.EntityId);
            else
                GridTerminalSystem.GetBlocksOfType(blocks, b => b.CubeGrid.EntityId == Me.CubeGrid.EntityId && b.CustomName == name);
            return blocks.FirstOrDefault();

        }
        List<T> GetBlocks<T> (string groupName = null) where T : class, IMyTerminalBlock
        {
            List<T> blocks;
            if (string.IsNullOrEmpty(groupName))
            {
                blocks = new List<T>();
                GridTerminalSystem.GetBlocksOfType(blocks, b => b.CubeGrid.EntityId == Me.CubeGrid.EntityId);
            }
            else
            {
                IMyBlockGroup group = GridTerminalSystem.GetBlockGroupWithName(groupName);
                if (group == null)
                    return new List<T>(0);
                blocks = new List<T>();
                group.GetBlocksOfType(blocks, b => b.CubeGrid.EntityId == Me.CubeGrid.EntityId);
            }
            return blocks;

        }
        #endregion

    }
}
