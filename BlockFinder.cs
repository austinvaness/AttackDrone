using Sandbox.ModAPI.Ingame;
using System.Collections.Generic;
using System.Linq;

namespace IngameScript
{
    public partial class Program
    {
        /* Usage:
                 * gridSystem = GridTerminalSystem;
                 * gridId = Me.CubeGrid.EntityId;
                 */
        static IMyGridTerminalSystem gridSystem;
        static long gridId;

        static T GetBlock<T> (string name, bool useSubgrids = false) where T : class, IMyTerminalBlock
        {
            if (useSubgrids)
            {
                return (T)gridSystem.GetBlockWithName(name);
            }
            else
            {
                List<T> blocks = GetBlocks<T>(false);
                foreach (T block in blocks)
                {
                    if (block.CustomName == name)
                        return block;
                }
                return null;
            }
        }
        static T GetBlock<T> (bool useSubgrids = false) where T : class, IMyTerminalBlock
        {
            List<T> blocks = GetBlocks<T>(useSubgrids);
            return blocks.FirstOrDefault();
        }
        static List<T> GetBlocks<T> (string groupName, bool useSubgrids = false) where T : class, IMyTerminalBlock
        {
            IMyBlockGroup group = gridSystem.GetBlockGroupWithName(groupName);
            List<T> blocks = new List<T>();
            group.GetBlocksOfType(blocks);
            if (!useSubgrids)
                blocks.RemoveAll(block => block.CubeGrid.EntityId != gridId);
            return blocks;

        }
        static List<T> GetBlocks<T> (bool useSubgrids = false) where T : class, IMyTerminalBlock
        {
            List<T> blocks = new List<T>();
            gridSystem.GetBlocksOfType(blocks);
            if (!useSubgrids)
                blocks.RemoveAll(block => block.CubeGrid.EntityId != gridId);
            return blocks;
        }
    }
}
