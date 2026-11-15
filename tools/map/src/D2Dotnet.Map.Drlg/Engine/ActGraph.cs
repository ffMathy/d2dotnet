// Port of libd2 packages/drlg/src/act.zig (MIT, (c) 2026 jaenster).
//
// Act-level DRLG graph: derives every level's world origin (x,y) + size (w,h) and the
// inter-level warp adjacency for one act, from the game's init seed.
//
// Two positioning paths, exactly as the engine:
//   - Levels listed in the act's placement graph get their origin from a backtracking placement
//     walk (DRLGLEVEL_ParseLevelData, 1.14d 006774xx): each node is placed adjacent to its
//     predecessor in an RNG-chosen direction, re-rolled on overlap until the whole chain fits.
//   - Every other level is offset-chained off its Levels.txt Depend
//     (DRLG_SetLevelPositionAndSize / DRLGLEVEL_SetLevelSizeAndAlignVsDependentLevel,
//     1.14d 00642d10): pos = Offset + depend.pos, recursively.
//
// The placement RNG runs off the act seed AFTER the act-specific pre-rolls (Act II
// Horadric/boss tomb selection, Act III jungle interlink) — see DRLG_AllocDrlgActMisc (1.14d
// 00642da0). Placement callbacks, the 8-direction tables and the FreeRoomEx overlap test are
// ported bit-exact from the binary.

namespace D2Dotnet.Map.Drlg.Engine;

/// <summary>A level rectangle in world tiles.</summary>
internal struct Coords
{
    public int x, y, w, h;
}

internal sealed class ActGraph
{
    private const int MAX_NODES = 15; // D2DrlgLevelPlacementStrc fixed arrays (0xf entries)

    // One placement callback used by the level-data graph. Names map to the binary:
    //   fixed   = fpLDE1                        (1.14d 006760f0) world origin = Levels.txt Offset
    //   adj2    = DRLGPLACE_Adjacent2DirRandom  (1.14d 006769a0) 2-side, 1 roll
    //   dir8    = fpLevelDataEntry              (1.14d 00676280) 8-dir half-size+8
    //   dir8a   = DRLGPLACE_Adjacent8DirAligned (1.14d 00676ae0) 8-roll, 4 aligned edges
    //   lde2    = fpLDE2                        (1.14d 00676150) 4-dir, 1 roll, +-0x10 nudge
    //   lde3    = fpLDE3                        (1.14d 00676650) 4-dir + flip, resizes (Blood Moor)
    //   lde4    = fpLDE4                        (1.14d 00676450) 4-dir + flip, +-8 nudge
    //   lde5    = fpLDE5                        (1.14d 006768c0) deterministic dir 0, NO roll
    //   mirror4 = DRLGPLACE_Adjacent4DirMirrored(1.14d 006762.. ) dir 3 fixed, 1 roll picks y-mirror
    //   ofix    = DRLGPLACE_OrientedFixedOffset (1.14d 0067d830) 1 roll: 90° orient + size, off prev
    //   oabs    = DRLGPLACE_OrientedAbsolutePos (1.14d 0067d8e0) 1 roll: orient + size, abs Offset
    //   otab    = DRLGPLACE_OrientedTableLookup (1.14d 0067d980) 2-dir orient, table-offset off prev
    private enum PlaceFn { Fixed, Adj2, Dir8, Dir8a, Lde2, Lde3, Lde4, Lde5, Mirror4, Ofix, Oabs, Otab }

    // Per-list validation gate (the fpLevelDataFn1 passed to ParseLevelData).
    //   none     = nullptr (Act V lists 1/2): first placement always accepted
    //   overlap  = DRLGPLACE_ACT{2,4}_Validate.. / ACT5_List3: no overlap w/ non-prev nodes
    //   act1_l1  = DRLGPLACE_ACT1_ValidatePlacementList1: overlap + Rogue-camp layout table +
    //              Burial-grounds direction-uniqueness gap
    //   act1_l2  = DRLGPLACE_ACT1_ValidatePlacementList2WithGap: overlap + a 200-tile clearance
    //              band above the cow level (node 0)
    private enum Validate { None, Overlap, Act1L1, Act1L2 }

    private readonly record struct Node(long LevelId, PlaceFn Place, int Prev, int Next);

    // Act II placement graph (DRLGACTMISC_AllocDrlgLevelForAct, ACT_II branch). List 1 is the
    // town→desert→snakes chain; list 2 is the lone Canyon of the Magi.
    private static readonly Node[] act2_list1 =
    {
        new(40, PlaceFn.Fixed, -1, -1), // Lut Gholein (town)
        new(41, PlaceFn.Adj2, 0, -1), // Rocky Waste
        new(42, PlaceFn.Dir8, 1, -1), // Dry Hills
        new(43, PlaceFn.Dir8, 2, -1), // Far Oasis
        new(44, PlaceFn.Dir8, 3, -1), // Lost City
        new(45, PlaceFn.Dir8a, 4, -1), // Valley of Snakes
    };
    private static readonly Node[] act2_list2 = { new(46, PlaceFn.Fixed, -1, -1) }; // Canyon of the Magi

    // Act I (ACT_I branch). List 1 is the wilderness trunk anchored at Stony Field; list 2 is the
    // Monastery approach + the cow level.
    private static readonly Node[] act1_list1 =
    {
        new(4, PlaceFn.Fixed, -1, -1), // Stony Field (anchor)
        new(3, PlaceFn.Lde2, 0, -1), // Cold Plains
        new(2, PlaceFn.Lde3, 1, -1), // Blood Moor (resizes)
        new(1, PlaceFn.Lde4, 2, -1), // Rogue Encampment (town)
        new(17, PlaceFn.Lde2, 1, -1), // Burial Grounds
    };
    private static readonly Node[] act1_list2 =
    {
        new(39, PlaceFn.Fixed, -1, -1), // Moo Moo Farm (cow level)
        new(26, PlaceFn.Fixed, -1, -1), // Monastery Gate
        new(7, PlaceFn.Lde5, 1, -1), // Tamoe Highland
        new(6, PlaceFn.Lde2, 2, -1), // Black Marsh
        new(5, PlaceFn.Lde2, 3, -1), // Dark Wood
    };

    // Act IV (ACT_IV branch). List 1 trunk off the Pandemonium Fortress; list 2 the lone Chaos
    // Sanctuary. Note: Act IV skips warp-wiring in ParseLevelData (nActNo==4).
    private static readonly Node[] act4_list1 =
    {
        new(103, PlaceFn.Fixed, -1, -1), // Pandemonium Fortress (town)
        new(104, PlaceFn.Mirror4, 0, -1), // Outer Steppes
        new(105, PlaceFn.Lde2, 1, -1), // Plains of Despair
        new(106, PlaceFn.Lde2, 2, -1), // City of the Damned
    };
    private static readonly Node[] act4_list2 = { new(108, PlaceFn.Fixed, -1, -1) }; // Chaos Sanctuary

    // Act V (ACT_V branch). List 1 the foothills trunk off Harrogath; list 2 the lone Frozen
    // Tundra (absolute-positioned); list 3 the Forgotten Sands / Uber Tristram pair. Lists 1 and 2
    // run with NO validation gate (first placement always taken).
    private static readonly Node[] act5_list1 =
    {
        new(109, PlaceFn.Fixed, -1, -1), // Harrogath (town)
        new(110, PlaceFn.Fixed, 0, -1), // Bloody Foothills
        new(111, PlaceFn.Ofix, 1, -1), // Frigid Highlands (orients)
        new(112, PlaceFn.Otab, 2, -1), // Arreat Plateau (orients)
    };
    private static readonly Node[] act5_list2 = { new(117, PlaceFn.Oabs, -1, -1) }; // Frozen Tundra (abs offset)
    private static readonly Node[] act5_list3 =
    {
        new(134, PlaceFn.Fixed, -1, -1), // Forgotten Sands (uber)
        new(136, PlaceFn.Fixed, -1, -1), // Uber Tristram
    };

    // DRLGPLACE_OrientedTableLookup offset table (1.14d data @ 006f1f7c, interleaved
    // gnDrlgLevelGenerationState/Flags). Indexed by curDir + prevDir*2; each entry is the (dx,dy)
    // of the placed level's origin relative to its predecessor's origin.
    private static readonly int[] orient_table_x = { 0, -96, -64, -160 };
    private static readonly int[] orient_table_y = { -160, -64, -96, 0 };

    // DRLGPLACE_ACT1_ValidatePlacementList1 RogueEncampentLayout[64] (1.14d .data): a gate on
    // (curDir,flip) of Rogue Encampment vs (curDir,flip) of its predecessor Blood Moor.
    // Index = curDir + 4*(flip + 2*(prevDir + 4*prevFlip)). 1 = allowed.
    private static readonly byte[] rogue_layout =
    {
        1, 1, 0, 0, 0, 0, 0, 0,
        0, 1, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 1, 0, 1, 0, 0,
        1, 0, 0, 0, 0, 0, 1, 1,
        0, 1, 0, 0, 0, 0, 0, 1,
        0, 1, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 1, 1, 0,
        1, 0, 0, 0, 0, 0, 1, 1,
    };

    // ---- DRLGLEVEL_ParseLevelData placement walk ---------------------------------------

    private sealed class Placement
    {
        public Seed seed;
        public readonly Node[] nodes;
        public readonly Coords[] coords = new Coords[MAX_NODES];
        public readonly int[] initial_dir = Fill(-1);
        public readonly int[] cur_dir = Fill(-1);
        public readonly int[] flip = Fill(-1); // aFlipState: rolled once, fixes cycle end
        public readonly int[] step = Fill(-1); // aStepCounter: alternates 0/1 each re-dispatch
        public int cur;

        private static int[] Fill(int v)
        {
            var a = new int[MAX_NODES];
            Array.Fill(a, v);
            return a;
        }

        public Placement(Seed s, Node[] n)
        {
            seed = s;
            nodes = n;
        }

        private ref Coords Prev(int node) => ref coords[nodes[node].Prev];

        /// <summary>fpLDE1: a fixed world origin straight from Levels.txt Offset. No RNG.</summary>
        private bool PlaceFixed(GameTables tb)
        {
            var def = tb.Level(nodes[cur].LevelId)!.Value;
            coords[cur].x = (int)def.OffsetX;
            coords[cur].y = (int)def.OffsetY;
            return true;
        }

        /// <summary>DRLGPLACE_Adjacent2DirRandom: place against the predecessor on one of two sides
        /// (left or above), the first chosen by a single RNG step.</summary>
        private bool PlaceAdj2()
        {
            int node = cur;
            if (initial_dir[node] == -1)
            {
                seed.Next();
                initial_dir[node] = (int)((seed.Low & 1) + 1);
                cur_dir[node] = initial_dir[node];
            }
            else
            {
                int nxt = 2 - (cur_dir[node] != 1 ? 1 : 0);
                if (nxt == initial_dir[node]) return false;
                cur_dir[node] = nxt;
            }
            ref var p = ref Prev(node);
            ref var s = ref coords[node];
            if (cur_dir[node] != 1)
            {
                switch (cur_dir[node])
                {
                    case 0: s.x = p.w - s.w + p.x; s.y = p.h + p.y; break;
                    case 2: s.x = p.x; s.y = p.y - s.h; break;
                    case 3: s.x = p.w + p.x; s.y = p.y; break;
                }
                return true;
            }
            s.x = p.x - s.w;
            s.y = p.y;
            return true;
        }

        /// <summary>Advance/seed an 8-way (mod-8) rotation; false once every direction is exhausted.
        /// Shared by fpLevelDataEntry and DRLGPLACE_Adjacent8DirAligned.</summary>
        private bool RollDir8()
        {
            int node = cur;
            if (initial_dir[node] == -1)
            {
                seed.Next();
                initial_dir[node] = (int)(seed.Low & 7);
                cur_dir[node] = initial_dir[node];
            }
            else
            {
                int nxt = (cur_dir[node] + 1) & 7;
                if (nxt == initial_dir[node]) return false;
                cur_dir[node] = nxt;
            }
            return true;
        }

        /// <summary>fpLevelDataEntry: 8 directions = 4 sides × 2 perpendicular shifts, each
        /// offsetting the half-size +8 tiles off the predecessor's matching edge.</summary>
        private bool PlaceDir8()
        {
            if (!RollDir8()) return false;
            int node = cur;
            ref var p = ref Prev(node);
            ref var s = ref coords[node];
            switch (cur_dir[node])
            {
                case 0: s.x = p.x - 8 - s.w / 2; s.y = p.y + p.h; break;
                case 1: s.x = p.x + s.w / 2 + 8; s.y = p.y + p.h; break;
                case 2: s.x = p.x - s.w; s.y = p.y - s.h / 2 - 8; break;
                case 3: s.x = p.x - s.w; s.y = p.y + s.h / 2 + 8; break;
                case 4: s.x = p.x - 8 - s.w / 2; s.y = p.y - s.h; break;
                case 5: s.x = p.x + s.w / 2 + 8; s.y = p.y - s.h; break;
                case 6: s.x = p.x + p.w; s.y = p.y - s.h / 2 - 8; break;
                case 7: s.x = p.x + p.w; s.y = p.y + s.h / 2 + 8; break;
            }
            return true;
        }

        /// <summary>DRLGPLACE_Adjacent8DirAligned: same 8-way roll, but the pairs collapse to the
        /// four edge-aligned placements (no perpendicular shift).</summary>
        private bool PlaceDir8Aligned()
        {
            if (!RollDir8()) return false;
            int node = cur;
            ref var p = ref Prev(node);
            ref var s = ref coords[node];
            switch (cur_dir[node])
            {
                case 0: case 1: s.x = p.x; s.y = p.h + p.y; break;
                case 2: case 3: s.x = p.x - s.w; s.y = p.y; break;
                case 4: case 5: s.x = p.x; s.y = p.y - s.h; break;
                case 6: case 7: s.x = p.w + p.x; s.y = p.y; break;
            }
            return true;
        }

        /// <summary>fpLDE2: place against the predecessor on one of four sides, chosen by a single
        /// mod-4 roll, each side nudged 0x10 tiles to leave a doorway gap.</summary>
        private bool PlaceLDE2()
        {
            int node = cur;
            if (initial_dir[node] == -1)
            {
                seed.Next();
                initial_dir[node] = (int)(seed.Low & 3);
                cur_dir[node] = initial_dir[node];
            }
            else
            {
                int nxt = (cur_dir[node] + 1) & 3;
                if (nxt == initial_dir[node]) return false;
                cur_dir[node] = nxt;
            }
            ref var p = ref Prev(node);
            ref var s = ref coords[node];
            switch (cur_dir[node])
            {
                case 0: s.x = p.x - 16; s.y = p.y + p.h; break;
                case 1: s.x = p.x - s.w; s.y = p.y - 16; break;
                case 2: s.x = p.x + p.w - s.w + 16; s.y = p.y - s.h; break;
                case 3: s.x = p.x + p.w; s.y = p.y + p.h - s.h + 16; break;
            }
            return true;
        }

        /// <summary>fpLDE5: deterministic — always direction 0 (below the predecessor), consumes NO
        /// RNG. Re-running on a backtrack is therefore a no-op on the seed.</summary>
        private bool PlaceLDE5()
        {
            int node = cur;
            initial_dir[node] = 0;
            cur_dir[node] = 0;
            ref var p = ref Prev(node);
            ref var s = ref coords[node];
            s.x = p.x;
            s.y = p.y + p.h;
            return true;
        }

        /// <summary>fpLDE3/fpLDE4 share this roll: on first entry, two RNG steps seed a direction
        /// (mod 4) and a flip (mod 2); each re-dispatch advances dir by the running step counter and
        /// toggles the step, exhausting after all 8 (dir,flip) pairs.</summary>
        private bool RollDir4Flip()
        {
            int node = cur;
            if (initial_dir[node] == -1)
            {
                seed.Next();
                initial_dir[node] = (int)(seed.Low & 3);
                cur_dir[node] = initial_dir[node];
                seed.Next();
                flip[node] = (int)(seed.Low & 1);
                step[node] = flip[node];
            }
            else
            {
                int nxtDir = (cur_dir[node] + step[node]) & 3;
                int nxtFlip = (step[node] + 1) & 1;
                if (nxtDir == initial_dir[node] && nxtFlip == flip[node]) return false;
                cur_dir[node] = nxtDir;
                step[node] = nxtFlip;
            }
            return true;
        }

        /// <summary>fpLDE4: 4-dir + flip placement, with an 8-tile gap nudge on the off-axis.</summary>
        private bool PlaceLDE4()
        {
            if (!RollDir4Flip()) return false;
            int node = cur;
            ref var p = ref Prev(node);
            ref var s = ref coords[node];
            if (step[node] != 0)
            {
                switch (cur_dir[node])
                {
                    case 0: s.x = p.x; s.y = p.y + p.h; break;
                    case 1: s.x = p.x - s.w; s.y = p.y + 8; break;
                    case 2: s.x = p.x + p.w - s.w; s.y = p.y - s.h; break;
                    case 3: s.x = p.x + p.w; s.y = p.y + p.h - s.h - 8; break;
                }
            }
            else
            {
                switch (cur_dir[node])
                {
                    case 0: s.x = p.x + p.w - s.w; s.y = p.y + p.h; break;
                    case 1: s.x = p.x - s.w; s.y = p.y + p.h - s.h - 8; break;
                    case 2: s.x = p.x; s.y = p.y - s.h; break;
                    case 3: s.x = p.x + p.w; s.y = p.y + 8; break;
                }
            }
            return true;
        }

        /// <summary>fpLDE3 (Blood Moor): 4-dir + flip, but first RESIZES the level by direction
        /// parity — odd dirs make it wide (0x60×0x38), even dirs tall (0x38×0x60) — then places with
        /// a 0x10 doorway nudge.</summary>
        private bool PlaceLDE3()
        {
            if (!RollDir4Flip()) return false;
            int node = cur;
            ref var s = ref coords[node];
            int parity = cur_dir[node] & 1;
            s.w = parity != 0 ? 0x60 : 0x38;
            s.h = parity != 0 ? 0x38 : 0x60;
            ref var p = ref Prev(node);
            if (step[node] != 0)
            {
                switch (cur_dir[node])
                {
                    case 0: s.x = p.x - 16; s.y = p.y + p.h; break;
                    case 1: s.x = p.x - s.w; s.y = p.y - 16; break;
                    case 2: s.x = p.x + p.w - s.w + 16; s.y = p.y - s.h; break;
                    case 3: s.x = p.x + p.w; s.y = p.y + p.h - s.h + 16; break;
                }
            }
            else
            {
                switch (cur_dir[node])
                {
                    case 0: s.x = p.x + p.w - s.w + 16; s.y = p.y + p.h; break;
                    case 1: s.x = p.x - s.w; s.y = p.y + p.h - s.h + 16; break;
                    case 2: s.x = p.x - 16; s.y = p.y - s.h; break;
                    case 3: s.x = p.x + p.w; s.y = p.y - 16; break;
                }
            }
            return true;
        }

        /// <summary>DRLGPLACE_Adjacent4DirMirrored (Outer Steppes): direction is fixed at 3 (right
        /// of predecessor); a single roll mirrors the vertical alignment. Always succeeds, so it
        /// re-rolls (and re-consumes RNG) every time the walk backtracks into it.</summary>
        private bool PlaceMirror4()
        {
            int node = cur;
            initial_dir[node] = 3;
            cur_dir[node] = 3;
            seed.Next();
            ref var p = ref Prev(node);
            ref var s = ref coords[node];
            s.x = p.x + p.w;
            s.y = (seed.Low & 1) != 0 ? p.y + p.h - s.h + 8 : p.y - 8;
            return true;
        }

        /// <summary>Oriented size: even roll => 0x40×0xa0 (tall), odd => 0xa0×0x40 (wide).</summary>
        private void OrientSize(int node, int dir)
        {
            ref var s = ref coords[node];
            if ((seed.Low & 1) == 0)
            {
                s.w = 0x40;
                s.h = 0xa0;
            }
            else if (dir != 0)
            {
                s.w = 0xa0;
                s.h = 0x40;
            }
        }

        /// <summary>DRLGPLACE_OrientedFixedOffset (Frigid Highlands): one roll picks a 90°
        /// orientation (and matching size); origin sits to the left of the predecessor,
        /// bottom-aligned.</summary>
        private bool PlaceOrientFixed()
        {
            int node = cur;
            seed.Next();
            int d = (int)(seed.Low & 1);
            cur_dir[node] = d;
            initial_dir[node] = d;
            OrientSize(node, d);
            ref var p = ref Prev(node);
            ref var s = ref coords[node];
            s.x = p.x - s.w;
            s.y = p.y + p.h - s.h - 16;
            return true;
        }

        /// <summary>DRLGPLACE_OrientedAbsolutePos (Frozen Tundra): same orient roll, but the origin
        /// is the level's absolute Levels.txt Offset (not relative to any predecessor).</summary>
        private bool PlaceOrientAbs(GameTables tb)
        {
            int node = cur;
            seed.Next();
            int d = (int)(seed.Low & 1);
            cur_dir[node] = d;
            initial_dir[node] = d;
            OrientSize(node, d);
            var def = tb.Level(nodes[node].LevelId)!.Value;
            ref var s = ref coords[node];
            s.x = (int)def.OffsetX;
            s.y = (int)def.OffsetY;
            return true;
        }

        /// <summary>DRLGPLACE_OrientedTableLookup (Arreat Plateau): a 2-way orient roll; the origin
        /// is the predecessor's origin plus a table offset keyed on (curDir, predecessor's dir).</summary>
        private bool PlaceOrientTable()
        {
            int node = cur;
            if (initial_dir[node] == -1)
            {
                seed.Next();
                initial_dir[node] = (int)(seed.Low & 1);
                cur_dir[node] = initial_dir[node];
            }
            else
            {
                int nxt = cur_dir[node] == 0 ? 1 : 0;
                if (nxt == initial_dir[node]) return false;
                cur_dir[node] = nxt;
            }
            int dir = cur_dir[node];
            ref var s = ref coords[node];
            if (dir == 0)
            {
                s.w = 0x40;
                s.h = 0xa0;
            }
            else
            {
                s.w = 0xa0;
                s.h = 0x40;
            }
            int prevDir = cur_dir[nodes[node].Prev];
            int idx = dir + prevDir * 2;
            ref var p = ref Prev(node);
            s.x = orient_table_x[idx] + p.x;
            s.y = orient_table_y[idx] + p.y;
            return true;
        }

        public bool Dispatch(GameTables tb) => nodes[cur].Place switch
        {
            PlaceFn.Fixed => PlaceFixed(tb),
            PlaceFn.Adj2 => PlaceAdj2(),
            PlaceFn.Dir8 => PlaceDir8(),
            PlaceFn.Dir8a => PlaceDir8Aligned(),
            PlaceFn.Lde2 => PlaceLDE2(),
            PlaceFn.Lde3 => PlaceLDE3(),
            PlaceFn.Lde4 => PlaceLDE4(),
            PlaceFn.Lde5 => PlaceLDE5(),
            PlaceFn.Mirror4 => PlaceMirror4(),
            PlaceFn.Ofix => PlaceOrientFixed(),
            PlaceFn.Oabs => PlaceOrientAbs(tb),
            PlaceFn.Otab => PlaceOrientTable(),
            _ => throw new InvalidOperationException(),
        };
    }

    /// <summary>DRLGCOORDS_IsWithinDistance(.., nSize=0) wrapped by FreeRoomEx (1.14d 0066b860):
    /// "free" (true) unless the two rects overlap on BOTH axes.</summary>
    private static bool FreeRoomEx(Coords a, Coords b)
    {
        int dx = a.x < b.x ? b.x - a.w - a.x : a.x - b.w - b.x;
        int dy = a.y < b.y ? b.y - a.h - a.y : a.y - b.h - b.y;
        return !(dx < 0 && dy < 0);
    }

    /// <summary>The common overlap gate: the freshly placed node must not overlap any
    /// already-placed node other than its own predecessor.</summary>
    private static bool ValidateNoOverlap(Placement p, int node)
    {
        for (int i = 0; i < node; i++)
            if (i != p.nodes[node].Prev && !FreeRoomEx(p.coords[node], p.coords[i])) return false;
        return true;
    }

    /// <summary>DRLGPLACE_ACT1_ValidatePlacementList1: overlap gate, plus two extra constraints.
    /// Rogue Encampment must land on an allowed (dir,flip)×(prevDir,prevFlip) combo per the layout
    /// table; Burial Grounds must not share a direction with its sibling (Blood Moor, the other
    /// branch off Cold Plains).</summary>
    private static bool ValidateAct1List1(Placement p, int node)
    {
        if (!ValidateNoOverlap(p, node)) return false;
        long lvl = p.nodes[node].LevelId;
        if (lvl == 1) // Rogue Encampment
        {
            int prev = p.nodes[node].Prev;
            int i = p.cur_dir[node] + 4 * (p.step[node] + 2 * (p.cur_dir[prev] + 4 * p.step[prev]));
            return rogue_layout[i] != 0;
        }
        if (lvl == 17) // Burial Grounds
        {
            int prevId = p.nodes[node].Prev;
            for (int i = 0; i < p.nodes.Length; i++)
                if (i != node && p.nodes[i].Prev == prevId && p.cur_dir[node] == p.cur_dir[i]) return false;
        }
        return true;
    }

    /// <summary>DRLGPLACE_ACT1_ValidatePlacementList2WithGap: overlap gate, plus a 200-tile
    /// clearance band raised above the cow level (node 0) that the placed node must also stay
    /// clear of.</summary>
    private static bool ValidateAct1List2(Placement p, int node)
    {
        if (!ValidateNoOverlap(p, node)) return false;
        if (node == 0) return true;
        var cow = p.coords[0];
        cow.h += 200;
        cow.y -= 200;
        return FreeRoomEx(cow, p.coords[node]);
    }

    private static bool RunValidate(Placement p, int node, Validate kind) => kind switch
    {
        Validate.None => true,
        Validate.Overlap => ValidateNoOverlap(p, node),
        Validate.Act1L1 => ValidateAct1List1(p, node),
        Validate.Act1L2 => ValidateAct1List2(p, node),
        _ => true,
    };

    /// <summary>Run one level-data list: size every node from Levels.txt, then walk the chain
    /// placing each node and backtracking on overlap. `seed` is taken by value (the engine copies
    /// the act seed into the placement state and never writes it back).</summary>
    private static Placement RunPlacement(GameTables tb, Node[] nodes, Seed seed, Validate validate)
    {
        var p = new Placement(seed, nodes);
        for (int i = 0; i < nodes.Length; i++)
        {
            var def = tb.Level(nodes[i].LevelId)!.Value;
            p.coords[i].w = (int)def.SizeX;
            p.coords[i].h = (int)def.SizeY;
        }

        int node = 0;
        while (node >= 0 && node < nodes.Length)
        {
            p.cur = node;
            if (!p.Dispatch(tb))
            {
                // Placement exhausted: clear this node's per-walk state and backtrack.
                p.initial_dir[node] = -1;
                p.cur_dir[node] = -1;
                p.flip[node] = -1;
                p.step[node] = -1;
                node -= 1;
            }
            else if (RunValidate(p, node, validate))
            {
                node += 1;
            }
            // else: placed but rejected by the gate — re-dispatch the same node next iteration.
        }
        return p;
    }

    private static void ParseLevelData(GameTables tb, Node[] nodes, Seed seed, Validate validate, Dictionary<long, Coords> outMap)
    {
        var p = RunPlacement(tb, nodes, seed, validate);
        for (int i = 0; i < nodes.Length; i++) outMap[nodes[i].LevelId] = p.coords[i];
    }

    /// <summary>DRLGLEVEL_ParseLevelData Cathedral→Courtyard write (1.14d 006772c0, pass 2,
    /// `eD2LevelId == 6` branch): the Black Marsh placement direction selects whether the act seed
    /// is rolled and which jail-exit variant (nPickedFile) the Courtyard (level 0x1b) receives.
    /// `dir == 1` → roll, pick = 2 - (newLow &amp; 1); `dir == 3` → roll, pick = ~newLow &amp; 1; any
    /// other direction → no write (null). The roll consumes pDrlg->nSeed, which the placement walk
    /// leaves untouched (it rolls a private copy), so it is the pristine ACT_I placement seed.</summary>
    public static int? Act1CourtyardPick(GameTables tb, uint initSeed)
    {
        var seed = PlacementSeed(0, initSeed); // == pDrlg->nSeed for an ACT_I game
        var p = RunPlacement(tb, act1_list2, seed, Validate.Act1L2);
        int dir = -1;
        for (int i = 0; i < act1_list2.Length; i++)
            if (act1_list2[i].LevelId == 6) dir = p.cur_dir[i]; // Black Marsh node
        if (dir != 1 && dir != 3) return null;
        var ns = seed; // the placement copy never wrote back, so this is still the act seed
        ns.Next();
        if (dir == 1) return 2 - (int)(ns.Low & 1);
        return (int)(~ns.Low & 1);
    }

    /// <summary>DRLGLEVEL_ParseLevelData Rogue-Encampment write (1.14d 006772c0, pass 2,
    /// `eD2LevelId == 1` branch): a PresetArea town takes its nPickedFile directly from its own
    /// placement direction (aCurrentDir[node]). Unconditional.</summary>
    public static int Act1TownPick(GameTables tb, uint initSeed)
    {
        var seed = PlacementSeed(0, initSeed);
        var p = RunPlacement(tb, act1_list1, seed, Validate.Act1L1);
        for (int i = 0; i < act1_list1.Length; i++)
            if (act1_list1[i].LevelId == 1) return p.cur_dir[i]; // Rogue Encampment node
        return 0;
    }

    /// <summary>DRLGLEVEL_ParseLevelData pass-2 write for Lut Gholein (1.14d 006772c0,
    /// `eD2LevelId == 0x28` branch): the Act-II town takes aCurrentDir[node+1] — the placement
    /// direction of the level placed directly AFTER it in the list (Rocky Waste).</summary>
    public static int Act2TownPick(GameTables tb, uint initSeed)
    {
        var seed = PlacementSeed(1, initSeed); // == pDrlg->nSeed for an ACT_II game
        var p = RunPlacement(tb, act2_list1, seed, Validate.Overlap);
        for (int i = 0; i < act2_list1.Length; i++)
        {
            if (act2_list1[i].LevelId == 40)
            {
                int nxt = i + 1;
                if (nxt < act2_list1.Length) return p.cur_dir[nxt];
            }
        }
        return 0;
    }

    /// <summary>The act seed that drives level placement: the init seed stepped once, then the
    /// act-specific pre-rolls of DRLG_AllocDrlgActMisc consumed.</summary>
    private static Seed PlacementSeed(long actNo, uint initSeed)
    {
        var s = new Seed(initSeed, 0x29a);
        s.Next(); // dwStartSeed capture (one step)
        switch (actNo)
        {
            case 1: // ACT_II: re-roll staff/boss tomb levels until they differ
                while (true)
                {
                    s.Next();
                    uint staff = s.Low % 7;
                    s.Next();
                    uint boss = s.Low % 7;
                    if (staff != boss) break;
                }
                break;
            case 2: // ACT_III: jungle-interlink bit
                s.Next();
                break;
        }
        return s;
    }

    // ---- built act ----------------------------------------------------------------------

    public long ActNo { get; }
    /// <summary>Level-RNG base (DRLG dwStartSeed).</summary>
    public uint StartSeed { get; }
    public Dictionary<long, Coords> Positions { get; } = new();
    public List<(long From, long To)> Warps { get; } = new();

    private ActGraph(long actNo, uint startSeed)
    {
        ActNo = actNo;
        StartSeed = startSeed;
    }

    /// <summary>World coords (origin + size) of any level in this act. Graph levels come from the
    /// placement walk; the rest fall back to the Depend offset chain.</summary>
    public Coords LevelCoords(GameTables tb, long levelId)
    {
        if (Positions.TryGetValue(levelId, out var c)) return c;
        if (tb.Level(levelId) is not LevelRow lv) return default;
        Coords b = default;
        if (lv.Depend != 0) b = LevelCoords(tb, lv.Depend);
        return new Coords { x = (int)lv.OffsetX + b.x, y = (int)lv.OffsetY + b.y, w = (int)lv.SizeX, h = (int)lv.SizeY };
    }

    /// <summary>Build an act's level graph for a game init seed. `actNo` is the 0-based act index
    /// (Levels.txt Act column): Act I = 0, Act II = 1, …</summary>
    public static ActGraph Build(GameTables tb, long actNo, uint initSeed)
    {
        var act = new ActGraph(actNo, Seed.ActStartSeed(initSeed));
        // Each ParseLevelData call copies the act seed afresh and never writes it back, so every
        // list of an act starts from the same placement seed.
        var seed = PlacementSeed(actNo, initSeed);
        switch (actNo)
        {
            case 0: // Act I
                ParseLevelData(tb, act1_list1, seed, Validate.Act1L1, act.Positions);
                ParseLevelData(tb, act1_list2, seed, Validate.Act1L2, act.Positions);
                AddChainWarps(act1_list1, act.Warps);
                AddChainWarps(act1_list2, act.Warps);
                break;
            case 1: // Act II
                ParseLevelData(tb, act2_list1, seed, Validate.Overlap, act.Positions);
                ParseLevelData(tb, act2_list2, seed, Validate.Overlap, act.Positions);
                AddChainWarps(act2_list1, act.Warps);
                AddChainWarps(act2_list2, act.Warps);
                break;
            // Act III (act_no 2) has no RNG placement walk: DRLGActMisc_InitAct3 pins Kurast
            // Docktown at its Levels.txt Offset and everything else rides the Depend chain, which
            // the LevelCoords fallback already reproduces.
            case 3: // Act IV
                ParseLevelData(tb, act4_list1, seed, Validate.Overlap, act.Positions);
                ParseLevelData(tb, act4_list2, seed, Validate.Overlap, act.Positions);
                AddChainWarps(act4_list1, act.Warps);
                AddChainWarps(act4_list2, act.Warps);
                break;
            case 4: // Act V
                ParseLevelData(tb, act5_list1, seed, Validate.None, act.Positions);
                ParseLevelData(tb, act5_list2, seed, Validate.None, act.Positions);
                ParseLevelData(tb, act5_list3, seed, Validate.Overlap, act.Positions);
                AddChainWarps(act5_list1, act.Warps);
                AddChainWarps(act5_list2, act.Warps);
                AddChainWarps(act5_list3, act.Warps);
                break;
        }
        return act;
    }

    /// <summary>The DRLGACT_SetWarpConnection wiring done inside ParseLevelData: a bidirectional
    /// warp between every node and its prev/next neighbour in the chain.</summary>
    private static void AddChainWarps(Node[] nodes, List<(long, long)> outList)
    {
        foreach (var n in nodes)
        {
            if (n.Prev >= 0)
            {
                long a = nodes[n.Prev].LevelId;
                outList.Add((a, n.LevelId));
                outList.Add((n.LevelId, a));
            }
        }
    }
}
