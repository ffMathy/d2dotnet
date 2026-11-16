// Port of libd2 packages/drlg/src/drlg/outdoors/OutJung.zig (MIT, (c) 2026 jaenster).
//
// Mechanical transform of the ACT-III (jungle/Kurast) outdoor generator.
//
// Sources:
//   OutJung::BuildJungle                       recon/closure/OutJung.cpp:42   (0067e910)
//   OutJung::SpawnRandomPreset                 recon/closure/OutJung.cpp:131  (0067eed0)
//   DRLGOUTJUNG_BuildKurastDocktownBorders     recon/closure/Drlg.cpp:5828    (0067ead0)
//   DRLGOUTJUNG_BuildKurastUpperBorders        recon/closure/Drlg.cpp:5872    (0067ec30)
//   DRLGOUTJUNG_BuildKurastCausewayBorders     recon/closure/Drlg.cpp:5912    (0067ed70)
//   OutPlace::BuildKurast                       recon/closure/OutPlace.cpp:1584 (0067f190)
//   OutPlace::BuildTravincal                    recon/closure/OutPlace.cpp:1642 (0067f3b0)
//   OutPlace::InitAct3OutdoorLevel              recon/closure/OutPlace.cpp:1657 (0067f450)
//   DRLGLEVEL_FixDrlgLevelForSpiderForest       0x00677880
//   DRLGLEVEL_FixDrlgLevelForLowerKurast        0x00678910
//
// .rdata artifacts recovered from Game.exe 1.14d (session 62fbfe69) — the decompiler rendered
// both as single-int static stubs (the classic .rdata window collapse):
//   * gnOutJungPresetOffsetByLevel  @0x6f2240, indexed [eD2LevelId] (ADD at 0x67ea64).
//     [0x4c]=0 [0x4d]=10 [0x4e]=20 — the per-jungle preset offset.
//   * gaOutJungSpecialPresetRandOffsets @0x6f2328, int[6][3] indexed
//     [nSeedModulo][nSpecialPresetCount] (MOV at 0x67ea73). The 6 rows are the permutations of
//     (0,1,2) — the seed-picked file-variant order for the 3 special presets.
//
// Faithful idioms:
//   * RNG -> the VERIFIED RANDOM_RandomNumberSelector primitive (the recon's inline
//     Fisher-Yates draws render the pow2 branch with a decompiler aliasing artifact).
//   * pAutomapEx (level +0x1bc) is the upstream jungle distribution map produced by the
//     Act-III sub-level splitter (FixDrlgLevelForSpiderForest).
//   * bJungleInterlink: InitAct3OutdoorLevel redoes the exact Act-III interlink roll
//     (Drlg.cpp:1014-1016) from pDrlg->dwGameLowSeed (the stored game seed).

namespace D2Dotnet.Map.Drlg.Engine.Outdoors;

internal static unsafe class OutJung
{
    private static D2DrlgLevelDataWildernessLevel* Wild(D2DrlgLevelStrc* pLevel) => (D2DrlgLevelDataWildernessLevel*)pLevel->pDrlgLevelData;

    /// <summary>Toward-zero division by 8 (recon `(v + (v>>31 &amp; 7)) >> 3`).</summary>
    private static int Div8(int v) => (v + (int)((uint)v >> 31) * 7) >> 3;

    /// <summary>Toward-zero division by 32 (recon `(v + (v>>31 &amp; 0x1f)) >> 5`).</summary>
    private static int Div32(int v) => (v + (int)((uint)v >> 31) * 0x1f) >> 5;

    /// <summary>gnOutJungPresetOffsetByLevel @0x6f2240 — reachable indices only.</summary>
    private static int JungPresetOffset(int nLevelId) => nLevelId switch { 0x4c => 0, 0x4d => 10, 0x4e => 20, _ => 0 };

    // gaOutJungSpecialPresetRandOffsets @0x6f2328 — int[6][3], the permutations of (0,1,2)
    // selected by [nSeedModulo].
    private static readonly int[,] gaOutJungSpecialPresetRandOffsets =
    {
        { 0, 1, 2 }, { 1, 0, 2 }, { 0, 2, 1 }, { 1, 2, 0 }, { 2, 0, 1 }, { 2, 1, 0 },
    };

    /// <summary>DRLGOUTPLACE_CalcSubLevelPlacement   0x006777d0. Register params ESI=nH, EDI=nW,
    /// EBX=pOut; hidden stack: [EBP+8]=pParentCoords, [EBP+0xc]=eSubLevelDir (= caller's
    /// nSeed.nSeedLow % 5 just before CALL). Directions: 0=SE, 1=NE, 2=SW, 3=NW, 4=case4.</summary>
    private static D2DrlgCoordsStrc CalcSubLevelPlacement(int nH, int nW, uint dir, int px, int py)
    {
        int offX = dir switch { 0 => 0, 1 => -nW, 2 => nW, 3 => -nW, 4 => nW, _ => throw new InvalidOperationException() };
        int offY;
        switch (dir)
        {
            case 0:
                offY = -nH;
                break;
            case 1:
            case 2:
                {
                    // recon OutPlace.cpp:611/615: ((int64*0x55555555)>>32 - nH) >> 1, then the
                    // common -= (y >> 31) correction (line 637).
                    int hi = (int)(((long)nH * 0x55555555L) >> 32);
                    int y0 = (hi - nH) >> 1;
                    offY = y0 - (y0 >> 31);
                    break;
                }
            case 3:
            case 4:
                {
                    // recon 619/623: (nH*-2)/3 + (nH*-2 >> 31), then -= (y >> 31) (637).
                    int t = nH * -2;
                    int y0 = t / 3 + (t >> 31);
                    offY = y0 - (y0 >> 31);
                    break;
                }
            default: throw new InvalidOperationException();
        }
        return new D2DrlgCoordsStrc
        {
            WorldPosition = new POINT { x = px + offX, y = py + offY },
            WorldSize = new POINT { x = nW, y = nH },
        };
    }

    // Tile lookup tables from binary at 0x6f13bc (T0, 16 entries) and 0x6f13f0 (tree, 15×4).
    // Columns 0..3 correspond to path-bit checks 0x10/0x20/0x40/0x80 in sequence.
    private static readonly uint[] T0 = { 0x100, 0x23f, 0x240, 0x241, 0x242, 0x243, 0x244, 0, 0x245, 0x246, 0x247, 0, 0x248, 0, 0, 0 };
    private static readonly uint[,] Tree =
    {
        { 0, 0, 0, 0 },
        { 0, 0x221, 0x222, 0x223 },
        { 0x224, 0, 0x225, 0x226 },
        { 0, 0, 0x227, 0x228 },
        { 0x229, 0x22a, 0, 0x22b },
        { 0, 0x22c, 0, 0x22d },
        { 0x22e, 0, 0, 0x22f },
        { 0, 0, 0, 0x230 },
        { 0x231, 0x232, 0x233, 0 },
        { 0, 0x234, 0x235, 0 },
        { 0x236, 0, 0x237, 0 },
        { 0, 0, 0x238, 0 },
        { 0x239, 0x23a, 0, 0 },
        { 0, 0x23b, 0, 0 },
        { 0x23c, 0, 0, 0 },
    };

    /// <summary>Bounded i32 read from a flat map (returns 0 for out-of-bounds).</summary>
    private static int Rd(int* buf, int len, int idx) => (idx < 0 || idx >= len) ? 0 : buf[idx];
    private static uint Rdu(uint* buf, int len, int idx) => (idx < 0 || idx >= len) ? 0 : buf[idx];

    /// <summary>DRLGLEVEL_FixDrlgLevelForSpiderForest   0x00677880. Called from
    /// DRLGActMisc_InitAct3 (0x006789b0) with pLevel = Kurast Docktown (L75/0x4b). Procedurally
    /// places 3 outdoor jungle sub-levels (0x4c..0x4e), carves inter-level paths, assigns
    /// pAutomapEx grid data, and returns the last allocated sub-level.</summary>
    public static D2DrlgLevelStrc* FixDrlgLevelForSpiderForest(D2DrlgLevelStrc* pLevel)
    {
        var pDrlg = pLevel->pDrlg;
        void* pPool = pDrlg->pMemoryPool;
        var pDefs = DrlgTables.LevelDefsGetLine(LevelIds.SpiderForest);
        int diff = pDrlg->nDifficulty;
        int sub_h = pDefs->SizeY[diff]; // 192
        int sub_w = pDefs->SizeX[diff]; // 64
        int cell_w = Div32(sub_w); // iVar4 = 2
        int cell_h = Div32(sub_h); // iVar5 = 6

        // Sub-level entry fields (local_120 equivalent, 3 entries × 14 i32)
        var ex = new int[3];
        var ey = new int[3];
        var ew = new int[3];
        var eh = new int[3];
        var edir = new int[3]; // [4]
        var en_exits = new int[3]; // [5]
        var eexits = new byte[3][] { new byte[3], new byte[3], new byte[3] }; // [7..9] child indices
        var egrid_col = new int[3]; // [10]
        var egrid_row1 = new int[3]; // [11] bNoOverlap
        var epdata = new nint[3]; // [12]
        var enspec = new int[3]; // [13]

        // Init entry 0 from pLevel (L75 / Kurast Docktown)
        ex[0] = pLevel->sCoordinatesAndSize.WorldPosition.x;
        ey[0] = pLevel->sCoordinatesAndSize.WorldPosition.y - sub_h;
        ew[0] = sub_w;
        eh[0] = sub_h;

        int nMinX = ex[0];
        int nMaxX = ex[0] + sub_w;
        int nMinY = ey[0];
        int nGridY_top = pLevel->sCoordinatesAndSize.WorldPosition.y;

        // PLACEMENT LOOP
        uint nSubLevelCount = 1;
        while (nSubLevelCount < 3)
        {
            uint nRandParent = DrlgRng.RandomNumberSelector(&pDrlg->nSeed, nSubLevelCount);
            // Second advance: direction = nSeedLow % 5
            var ds = DrlgRng.SeedNext(pDrlg->nSeed);
            pDrlg->nSeed = ds;
            uint dir = (uint)ds.nSeedLow % 5;

            var cand = CalcSubLevelPlacement(sub_h, sub_w, dir, ex[nRandParent], ey[nRandParent]);

            // Overlap check against all existing entries
            bool overlap = false;
            for (uint i = 0; i < nSubLevelCount; i++)
            {
                var c1 = new D2DrlgCoordsStrc { WorldPosition = new POINT { x = ex[i], y = ey[i] }, WorldSize = new POINT { x = ew[i], y = eh[i] } };
                var c2 = cand;
                if (DrlgRoom.FreeRoomEx(&c1, &c2, 0) == 0) { overlap = true; break; }
            }
            if (overlap) continue;

            // Accept placement
            byte ni = (byte)nSubLevelCount;
            eexits[nRandParent][en_exits[nRandParent]] = ni;
            en_exits[nRandParent] += 1;
            ex[ni] = cand.WorldPosition.x;
            ey[ni] = cand.WorldPosition.y;
            ew[ni] = cand.WorldSize.x;
            eh[ni] = cand.WorldSize.y;
            edir[ni] = (int)dir;

            if (cand.WorldPosition.x < nMinX) nMinX = cand.WorldPosition.x;
            if (cand.WorldPosition.y < nMinY) nMinY = cand.WorldPosition.y;
            if (cand.WorldPosition.x + cand.WorldSize.x > nMaxX) nMaxX = cand.WorldPosition.x + cand.WorldSize.x;
            nSubLevelCount += 1;
        }

        // Alignment assertion (non-multiple of 32 is a fatal error in the binary)
        if ((nMaxX - nMinX) % 32 != 0 || (nGridY_top - nMinY) % 32 != 0) return null;

        int grid_w = Div32(nMaxX - nMinX) + 2;
        int grid_h = Div32(nGridY_top - nMinY) + 2;
        int map_size = grid_h * grid_w;

        // Allocate 4 scratch maps (AllocServerMemory = calloc)
        int* pSubLevelMap = (int*)Pool.AllocServerMemory(pPool, (nuint)(map_size * 4));
        int* pDistWorkMap = (int*)Pool.AllocServerMemory(pPool, (nuint)(map_size * 4));
        int* pDistMap = (int*)Pool.AllocServerMemory(pPool, (nuint)(map_size * 4));
        uint* pSLDM = (uint*)Pool.AllocServerMemory(pPool, (nuint)(map_size * 4));

        // FLOOD FILL (outer retry loop, goto LAB_00677b80)
        while (true)
        {
            new Span<int>(pSubLevelMap, map_size).Clear();
            new Span<int>(pDistWorkMap, map_size).Clear();
            new Span<int>(pDistMap, map_size).Clear();
            new Span<uint>(pSLDM, map_size).Clear();

            for (uint pSubIdx = 0; pSubIdx < 3; pSubIdx++)
            {
                // Grid position of this sub-level
                int gc = Div32(ex[pSubIdx] - nMinX) + 1; // grid_col (nGridY in C)
                int gr = Div32(ey[pSubIdx] - nMinY); // row (nGridX in C)
                int row1 = gr + 1; // bNoOverlap
                int rowN = gr + cell_h; // nBottomRow

                egrid_col[pSubIdx] = gc;
                egrid_row1[pSubIdx] = row1;

                // Allocate per-sub-level distribution grid
                int pgd_size = cell_h * cell_w;
                epdata[pSubIdx] = (nint)Pool.AllocServerMemory(pPool, (nuint)(pgd_size * 4));

                // nFlipState: for sub-level 0 draw seed; for others use direction parity
                int nFlipState;
                if (pSubIdx == 0)
                {
                    var fs = DrlgRng.SeedNext(pDrlg->nSeed);
                    pDrlg->nSeed = fs;
                    nFlipState = fs.nSeedLow & 1;
                }
                else
                {
                    // parity: (dir & 0x80000001) adjusted == 1
                    uint v = (uint)edir[pSubIdx];
                    uint masked = v & 0x80000001;
                    uint adj = (int)masked < 0 ? unchecked(((masked - 1) | 0xfffffffe) + 1) : masked;
                    nFlipState = adj == 1 ? 1 : 0;
                }

                // FLOOD FILL (path carve)   live Ghidra 0x677880. The cell-fill, entrance/tunnel
                // and parent-exit marking re-run each FLOOD iteration (pDistMap is recleared for
                // this sub-level's cells), nFlipState persists+mutates across iterations. The loop
                // repeats until one walk carves enough laterals (nRandIdx >= 2). NOTE: nRandIdx
                // seeds to (pSubIdx != 0) — a BOOLEAN, not pSubIdx (live binary is
                // `(uint)(pSubIdx != 0)`).
                uint nRandIdx = pSubIdx != 0 ? 1u : 0u;
                while (true)
                {
                    // Reclear this sub-level's cells (recon 905-926).
                    for (int r = row1; r <= rowN; r++)
                    {
                        for (int c = gc; c < gc + cell_w; c++)
                        {
                            int fi = r * grid_w + c;
                            pSubLevelMap[fi] = (int)(pSubIdx + 1);
                            pDistWorkMap[fi] = 0;
                            pDistMap[fi] = 0;
                            pSLDM[fi] = 0;
                        }
                    }

                    int nFlip = nFlipState; // nConnFlags
                    int nExitCol = nFlip + gc;

                    // Entrance + tunnel for sub-levels 1..2 (recon 929-932)
                    if (pSubIdx > 0)
                    {
                        int bot_flat = grid_w * rowN + nExitCol;
                        if (bot_flat >= 0 && bot_flat < map_size) pDistMap[bot_flat] = 1;
                        int tun_flat = (nFlip == 0 ? 1 : 0) * 2 - 1 + bot_flat;
                        if (tun_flat >= 0 && tun_flat < map_size) pDistMap[tun_flat] = 2;
                    }

                    // Mark exits from parent(s) in pDistMap. Live Ghidra (0x677880) has 5
                    // child-direction cases. nGridY=gc, nGridX=gr, bNoOverlap=row1.
                    for (int ei = 0; ei < en_exits[pSubIdx]; ei++)
                    {
                        byte ci = eexits[pSubIdx][ei]; // child index
                        int cd = edir[ci]; // child direction
                        int fi = cd switch
                        {
                            0 => row1 * grid_w + gc,
                            1 => (gr + 4) * grid_w + gc,
                            2 => (gr + 4) * grid_w + gc + 1,
                            3 => (gr + 2) * grid_w + gc,
                            4 => (gr + 2) * grid_w + gc + 1,
                            _ => -1,
                        };
                        if (cd >= 0 && cd <= 4 && fi >= 0 && fi < map_size) pDistMap[fi] = 1;
                    }

                    // PATH-CARVE WALK (live Ghidra 0x677e..)
                    int nExitCnt = 0; // pExitCount (reused as an int counter)
                    nRandIdx = pSubIdx != 0 ? 1u : 0u; // live: (uint)(pSubIdx != 0)
                    if (row1 <= rowN)
                    {
                        int nCellIdx = grid_w * rowN;
                        int nAdjIdx = ((int)pSubIdx + 1) * 100;
                        int nRowIter = rowN;
                        while (row1 <= nRowIter)
                        {
                            int fi = nExitCol + nCellIdx;
                            if (fi >= 0 && fi < map_size) pDistWorkMap[fi] = nAdjIdx;
                            nAdjIdx += 1;

                            // bLateral path = (nExitCnt==0 || nRowIter==row1); else draw a seed:
                            // %3!=0 also takes the lateral path, %3==0 flips column.
                            bool bLateral = nExitCnt == 0 || nRowIter == row1;
                            if (!bLateral)
                            {
                                var ds2 = DrlgRng.SeedNext(pDrlg->nSeed);
                                pDrlg->nSeed = ds2;
                                nFlip = nFlipState; // recon 980: nConnFlags = nFlipState
                                uint lw = (uint)ds2.nSeedLow;
                                if (lw % 3 != 0)
                                {
                                    bLateral = true; // goto LAB_00677e9f
                                }
                                else
                                {
                                    nExitCnt = 0;
                                    if (nFlipState == 0)
                                    {
                                        nFlip = 1;
                                        nExitCol += 1;
                                        nFlipState = 1;
                                    }
                                    else
                                    {
                                        nExitCol -= 1;
                                        nFlip = 0;
                                        nFlipState = 0;
                                    }
                                }
                            }

                            if (bLateral)
                            {
                                nExitCnt += 1;
                                if (nExitCnt > 1 && nRowIter > 1)
                                {
                                    int iVar7 = (nFlip == 0 ? 1 : 0) * 2 - 1 + nCellIdx + nExitCol;
                                    if (iVar7 >= 0 && iVar7 < map_size && pDistMap[iVar7] == 0)
                                    {
                                        nRandIdx += 1;
                                        pDistMap[iVar7] = 2;
                                    }
                                }
                                nRowIter -= 1;
                                nCellIdx -= grid_w;
                            }
                        }
                    }

                    if ((int)nRandIdx >= 2) break; // recon: while(nRandIdx<2)
                }

                // Trim excess exits to 3 (recon 1001-1054): while nRandIdx>3, pick the
                // nRandExitIdx-th pDistMap==2 cell WITHIN this sub-level's grid, zero it, --.
                while ((int)nRandIdx > 3)
                {
                    uint pick = DrlgRng.RandomNumberSelector(&pDrlg->nSeed, nRandIdx);
                    uint found = 0;
                    bool removed = false;
                    for (int rr = 0; rr < cell_h && !removed; rr++)
                    {
                        for (int cc = 0; cc < cell_w; cc++)
                        {
                            int fi2 = row1 * grid_w + gc + rr * grid_w + cc;
                            if (fi2 >= 0 && fi2 < map_size && pDistMap[fi2] == 2)
                            {
                                if (found == pick)
                                {
                                    pDistMap[fi2] = 0;
                                    nRandIdx -= 1;
                                    removed = true;
                                    break;
                                }
                                found += 1;
                            }
                        }
                    }
                    if (!removed) break; // safety: no ==2 cell found
                }
            }

            // ADJACENCY PASS
            for (int row = 0; row < grid_h; row++)
            {
                for (int col = 0; col < grid_w; col++)
                {
                    int flat = row * grid_w + col;
                    int cell_sub = pSubLevelMap[flat];
                    uint cf = 0; // pCellPtr (flags for this cell)

                    if (pDistMap[flat] == 1)
                    {
                        // Find best same-sub-level neighbor by minimum pDistWorkMap distance
                        int best = 0x7fffffff;
                        uint pCellPtr = 0;
                        uint pExitCount = 0;

                        // NORTH: unconditional check (no comma operator)
                        int n_flat = (row - 1) * grid_w + col;
                        if (row > 0)
                        {
                            int nd = pDistWorkMap[n_flat];
                            if (nd != 0 && nd < 0x7fffffff && pSubLevelMap[n_flat] == cell_sub)
                            {
                                pCellPtr = 8; pExitCount = 8; best = nd;
                            }
                        }

                        // SOUTH: comma operator — pCellPtr = pExitCount always when closer
                        int s_flat = (row + 1) * grid_w + col;
                        if (row + 1 < grid_h)
                        {
                            int sd = pDistWorkMap[s_flat];
                            if (sd != 0 && sd < best)
                            {
                                pCellPtr = pExitCount; // comma assignment
                                if (pSubLevelMap[s_flat] == cell_sub)
                                {
                                    pExitCount = 4; pCellPtr = 4; best = sd;
                                }
                            }
                        }

                        // EAST: comma operator
                        if (col + 1 < grid_w)
                        {
                            int ed = pDistWorkMap[flat + 1];
                            if (ed != 0 && ed < best)
                            {
                                pCellPtr = pExitCount; // comma
                                if (pSubLevelMap[flat + 1] == cell_sub)
                                {
                                    best = ed; pCellPtr = 2; // pExitCount NOT updated
                                }
                            }
                        }

                        // WEST: no comma, no pExitCount update
                        int w_flat = flat - 1;
                        if (Rd(pDistWorkMap, map_size, w_flat) != 0 && Rd(pDistWorkMap, map_size, w_flat) < best && Rd(pSubLevelMap, map_size, w_flat) == cell_sub)
                            pCellPtr = 1;
                        cf = pCellPtr;

                        // Set symmetric bits in pSLDM for neighbors
                        if ((cf & 8) != 0 && row > 0) pSLDM[n_flat] |= 4;
                        if ((cf & 4) != 0 && row + 1 < grid_h) pSLDM[s_flat] |= 8;
                        if ((cf & 2) != 0 && col + 1 < grid_w) pSLDM[flat + 1] |= 1;
                        // West write uses flat-1 (pFlagsRow pointer offset)
                        if ((cf & 1) != 0 && w_flat >= 0) pSLDM[w_flat] |= 2;

                        // Cross-boundary (different sub-level, pDistMap==1)
                        if (row > 0 && pDistMap[n_flat] == 1 && pSubLevelMap[n_flat] != cell_sub) cf |= 8;
                        if (row + 1 < grid_h && pDistMap[s_flat] == 1 && pSubLevelMap[s_flat] != cell_sub) cf |= 4;
                        if (col + 1 < grid_w && pDistMap[flat + 1] == 1 && pSubLevelMap[flat + 1] != cell_sub) cf |= 2;
                        if (Rd(pDistMap, map_size, w_flat) == 1 && Rd(pSubLevelMap, map_size, w_flat) != cell_sub) cf |= 1;
                    }

                    // pDistWorkMap path adjacency (diff-by-1 = connected path)
                    int dw = pDistWorkMap[flat];
                    if (dw != 0)
                    {
                        if (row > 0)
                        {
                            int nd = pDistWorkMap[(row - 1) * grid_w + col];
                            if (Math.Abs((long)dw - nd) == 1) cf |= 8;
                        }
                        if (row + 1 < grid_h)
                        {
                            int sd = pDistWorkMap[(row + 1) * grid_w + col];
                            if (Math.Abs((long)dw - sd) == 1) cf |= 4;
                        }
                        if (col + 1 < grid_w)
                        {
                            int ed = pDistWorkMap[flat + 1];
                            if (Math.Abs((long)dw - ed) == 1) cf |= 2;
                        }
                        int wd = Rd(pDistWorkMap, map_size, flat - 1);
                        if (Math.Abs((long)dw - wd) == 1) cf |= 1;
                    }

                    pSLDM[flat] |= cf;
                }
            }

            // PATH BITS PASS
            bool retry_flood = false;
            for (int row = 0; row < grid_h && !retry_flood; row++)
            {
                for (int col = 0; col < grid_w; col++)
                {
                    int flat = row * grid_w + col;
                    uint nConnFlags = pSLDM[flat];
                    int bNoOverlap_sub = pSubLevelMap[flat]; // sub-level ID

                    // Always advance seed for rotation bits
                    var dseed = DrlgRng.SeedNext(pDrlg->nSeed);
                    pDrlg->nSeed = dseed;
                    uint uVar8 = (uint)dseed.nSeedLow & 3;
                    bool bSwapped = nConnFlags == 0; // original flags were 0

                    if (pDistMap[flat] == 2) // tunnel cell
                    {
                        // First: find same-sub-level neighbor with 0 < pSLDM < 0xf
                        bool bFoundExit = true;
                        for (uint pi = 0; pi < 4; pi++)
                        {
                            if (!bFoundExit) break;
                            uint d = (pi + uVar8) & 3;
                            switch (d)
                            {
                                case 0:
                                    {
                                        int nf = (row - 1) * grid_w + col;
                                        if (row > 0 && pSubLevelMap[nf] == bNoOverlap_sub)
                                        {
                                            uint v = pSLDM[nf];
                                            if (v != 0 && v < 0xf) { nConnFlags |= 0x80; bFoundExit = false; }
                                        }
                                        break;
                                    }
                                case 1:
                                    {
                                        int nf = (row + 1) * grid_w + col;
                                        if (row + 1 < grid_h && pSubLevelMap[nf] == bNoOverlap_sub)
                                        {
                                            uint v = pSLDM[nf];
                                            if (v != 0 && v < 0xf) { nConnFlags |= 0x40; bFoundExit = false; }
                                        }
                                        break;
                                    }
                                case 2:
                                    if (col + 1 < grid_w && pSubLevelMap[flat + 1] == bNoOverlap_sub)
                                    {
                                        uint v = pSLDM[flat + 1];
                                        if (v != 0 && v < 0xf) { nConnFlags |= 0x20; bFoundExit = false; }
                                    }
                                    break;
                                case 3:
                                    if (flat - 1 >= 0 && pSubLevelMap[flat - 1] == bNoOverlap_sub)
                                    {
                                        uint v = pSLDM[flat - 1];
                                        if (v != 0 && v < 0xf) { nConnFlags |= 0x10; bFoundExit = false; }
                                    }
                                    break;
                            }
                        }

                        if (nConnFlags == 0) { retry_flood = true; break; } // LAB_00677b80

                        if (bSwapped)
                        {
                            // Cross-sub-level via pDistMap==2
                            bool bSwapped2 = true;
                            for (uint pi2 = 0; pi2 < 4; pi2++)
                            {
                                if (!bSwapped2) break;
                                uint d = (pi2 + uVar8) & 3;
                                switch (d)
                                {
                                    case 0:
                                        {
                                            int nf = (row - 1) * grid_w + col;
                                            if (row > 0 && pSubLevelMap[nf] != bNoOverlap_sub && pDistMap[nf] == 2) { nConnFlags |= 0x80; bSwapped2 = false; }
                                            break;
                                        }
                                    case 1:
                                        {
                                            int nf = (row + 1) * grid_w + col;
                                            if (row + 1 < grid_h && pSubLevelMap[nf] != bNoOverlap_sub && pDistMap[nf] == 2) { nConnFlags |= 0x40; bSwapped2 = false; }
                                            break;
                                        }
                                    case 2:
                                        if (col + 1 < grid_w && pSubLevelMap[flat + 1] != bNoOverlap_sub && pDistMap[flat + 1] == 2) { nConnFlags |= 0x20; bSwapped2 = false; }
                                        break;
                                    case 3:
                                        if (flat - 1 >= 0 && pSubLevelMap[flat - 1] != bNoOverlap_sub && pDistMap[flat - 1] == 2) { nConnFlags |= 0x10; bSwapped2 = false; }
                                        break;
                                }
                            }

                            // Advance seed twice; second advance used for extra search rotation
                            var ds3 = DrlgRng.SeedNext(pDrlg->nSeed);
                            pDrlg->nSeed = ds3;
                            bool bFoundExit2 = false;
                            if (!((ds3.nSeedLow & 1) == 0) && bSwapped2) bFoundExit2 = true;
                            var ds4 = DrlgRng.SeedNext(pDrlg->nSeed);
                            pDrlg->nSeed = ds4;

                            if (bFoundExit2)
                            {
                                // Extra same-sub-level search with new seed rotation
                                uint uVar8b = (uint)ds4.nSeedLow & 3;
                                for (uint pi3 = 0; pi3 < 4; pi3++)
                                {
                                    if (!bFoundExit2) break;
                                    uint d = (uVar8b + pi3) & 3;
                                    switch (d)
                                    {
                                        case 0:
                                            {
                                                int nf = (row - 1) * grid_w + col;
                                                if (row > 0 && (nConnFlags & 0x80) == 0)
                                                {
                                                    uint v = Rdu(pSLDM, map_size, nf);
                                                    if (v != 0 && v < 0xf) { nConnFlags |= 0x80; bFoundExit2 = false; }
                                                }
                                                break;
                                            }
                                        case 1:
                                            {
                                                int nf = (row + 1) * grid_w + col;
                                                if (row + 1 < grid_h && (nConnFlags & 0x40) == 0)
                                                {
                                                    uint v = Rdu(pSLDM, map_size, nf);
                                                    if (v != 0 && v < 0xf) { nConnFlags |= 0x40; bFoundExit2 = false; }
                                                }
                                                break;
                                            }
                                        case 2:
                                            if (col + 1 < grid_w && (nConnFlags & 0x20) == 0)
                                            {
                                                uint v = pSLDM[flat + 1];
                                                if (v != 0 && v < 0xf) { nConnFlags |= 0x20; bFoundExit2 = false; }
                                            }
                                            break;
                                        case 3:
                                            if (flat - 1 >= 0 && (nConnFlags & 0x10) == 0)
                                            {
                                                uint v = pSLDM[flat - 1];
                                                if (v != 0 && v < 0xf) { nConnFlags |= 0x10; bFoundExit2 = false; }
                                            }
                                            break;
                                    }
                                }
                            }
                        }

                        // Propagate path bits to neighbors
                        if ((nConnFlags & 0x80) != 0 && row > 0) pSLDM[(row - 1) * grid_w + col] |= 0x40;
                        if ((nConnFlags & 0x40) != 0 && row + 1 < grid_h) pSLDM[(row + 1) * grid_w + col] |= 0x80;
                        if ((nConnFlags & 0x20) != 0 && col + 1 < grid_w) pSLDM[flat + 1] |= 0x10;
                        if ((nConnFlags & 0x10) != 0 && flat - 1 >= 0) pSLDM[flat - 1] |= 0x20;
                        pDistMap[flat] = 0; // clear tunnel marker
                    }

                    pSLDM[flat] |= nConnFlags;
                }
            }

            if (retry_flood) continue;

            // TILE LOOKUP PASS
            for (int tile_i = 0; tile_i < map_size; tile_i++)
            {
                uint nc = pSLDM[tile_i];
                uint lo = nc & 0x0f;
                uint result = 0;
                if (lo == 0)
                {
                    if (nc > 0xf) result = T0[nc >> 4];
                }
                else if (nc < 0x10)
                {
                    result = lo + 0x211;
                }
                else
                {
                    uint uv = lo;
                    if ((nc & 0x10) != 0) uv = Tree[uv, 0];
                    if ((nc & 0x20) != 0) uv = Tree[uv, 1];
                    if ((nc & 0x40) != 0) uv = Tree[uv, 2];
                    if ((nc & 0x80) != 0) uv = Tree[uv, 3];
                    result = uv;
                }
                pSLDM[tile_i] = result;
            }

            // COPY TO PER-SUB-LEVEL GRIDS
            for (int si = 0; si < 3; si++)
            {
                int gc2 = egrid_col[si];
                int gr2 = egrid_row1[si]; // = bNoOverlap = div32(y-nMinY)+1
                uint* pgd = (uint*)epdata[si];
                int idx6 = 0;
                for (int gy = 0; gy < cell_h; gy++)
                {
                    for (int gx = 0; gx < cell_w; gx++)
                    {
                        uint val = pSLDM[(gr2 + gy) * grid_w + gc2 + gx];
                        pgd[idx6] = val;
                        if (val > 0x23e) enspec[si] += 1;
                        idx6 += 1;
                    }
                }
            }

            // BUBBLE SORT by y descending (entry[i].y >= entry[i+1].y)
            bool sorted = false;
            while (!sorted)
            {
                sorted = true;
                for (int si2 = 0; si2 < 2; si2++)
                {
                    if (ey[si2] < ey[si2 + 1])
                    {
                        // Swap all entry fields
                        (ex[si2], ex[si2 + 1]) = (ex[si2 + 1], ex[si2]);
                        (ey[si2], ey[si2 + 1]) = (ey[si2 + 1], ey[si2]);
                        (ew[si2], ew[si2 + 1]) = (ew[si2 + 1], ew[si2]);
                        (eh[si2], eh[si2 + 1]) = (eh[si2 + 1], eh[si2]);
                        (edir[si2], edir[si2 + 1]) = (edir[si2 + 1], edir[si2]);
                        (en_exits[si2], en_exits[si2 + 1]) = (en_exits[si2 + 1], en_exits[si2]);
                        (eexits[si2], eexits[si2 + 1]) = (eexits[si2 + 1], eexits[si2]);
                        (egrid_col[si2], egrid_col[si2 + 1]) = (egrid_col[si2 + 1], egrid_col[si2]);
                        (egrid_row1[si2], egrid_row1[si2 + 1]) = (egrid_row1[si2 + 1], egrid_row1[si2]);
                        (epdata[si2], epdata[si2 + 1]) = (epdata[si2 + 1], epdata[si2]);
                        (enspec[si2], enspec[si2 + 1]) = (enspec[si2 + 1], enspec[si2]);
                        sorted = false;
                    }
                }
            }

            // ASSIGN TO LEVELS 0x4c..0x4e
            D2DrlgLevelStrc* pSubLevelRet = null;
            for (int lvl_i = 0; lvl_i < 3; lvl_i++)
            {
                pSubLevelRet = Drlg.GetLevelAndAlloc(pDrlg, lvl_i + 0x4c);
                pSubLevelRet->pAutomapEx = (void*)epdata[lvl_i];
                pSubLevelRet->nUnknown_1B8 = (uint)enspec[lvl_i];
                pSubLevelRet->sCoordinatesAndSize.WorldPosition.x = ex[lvl_i];
                pSubLevelRet->sCoordinatesAndSize.WorldPosition.y = ey[lvl_i];
                pSubLevelRet->sCoordinatesAndSize.WorldSize.x = ew[lvl_i];
                pSubLevelRet->sCoordinatesAndSize.WorldSize.y = eh[lvl_i];
            }

            Pool.FreeServerMemory(pPool, pSubLevelMap);
            Pool.FreeServerMemory(pPool, pDistWorkMap);
            Pool.FreeServerMemory(pPool, pDistMap);
            Pool.FreeServerMemory(pPool, pSLDM);
            return pSubLevelRet;
        }
    }

    /// <summary>OutJung::BuildJungle   OutJung.cpp:42 (0067e910). Stamps the jungle preset layout
    /// (Spider Forest / Great Marsh / Flayer Jungle) from the upstream distribution map
    /// pAutomapEx. Special presets (id > 0x23e) are shifted by the per-jungle offset and assigned
    /// a seed-picked file variant.</summary>
    public static void BuildJungle(D2DrlgLevelStrc* pLevel)
    {
        int eLevelId = pLevel->eD2LevelId;
        if ((uint)(eLevelId - LevelIds.SpiderForest) >= 3) return;

        // Binary 0x0067e983: seed draw happens BEFORE the null check at 0x0067e98a.
        var pDefs = DrlgTables.LevelDefsGetLine(LevelIds.SpiderForest);
        int byDifficulty = pLevel->pDrlg->nDifficulty;
        int nLevelWidthCells = Div32(pDefs->SizeX[byDifficulty]);
        int nLevelHeightCells = Div32(pDefs->SizeY[byDifficulty]);
        int nPresetArrayIdx = 0;
        int nSeedModulo = (int)DrlgRng.GetModuloFromSeed(&pLevel->sSeed, (pLevel->nUnknown_1B8 == 3 ? 1u : 0u) * 4 + 2);

        if (pLevel->pAutomapEx == null) return;
        int* pMap = (int*)pLevel->pAutomapEx;
        int nSpecialPresetCount = 0;
        if (0 >= nLevelHeightCells) return;

        int nYCell = 0;
        while (true)
        {
            if (eLevelId == LevelIds.SpiderForest && nYCell == nLevelHeightCells - 1)
            {
                int lastCell = pMap[nLevelHeightCells * nLevelWidthCells - 1];
                OutPlace.SpawnOutdoorLevelPresetEx(pLevel, 0, nYCell * 4, 0x23d, lastCell == 0 ? 1 : 0, 0);
                nPresetArrayIdx += 2;
            }
            else if (eLevelId == LevelIds.FlayerJungle && nYCell == 0)
            {
                OutPlace.SpawnOutdoorLevelPresetEx(pLevel, 0, 0, 0x23e, pMap[1] == 0 ? 1 : 0, 0);
                nPresetArrayIdx += 2;
            }
            else
            {
                for (int nXCell = 0; nXCell < nLevelWidthCells; nXCell++)
                {
                    int nLevelPrestId = pMap[nPresetArrayIdx];
                    nPresetArrayIdx += 1;
                    int nRand = -1;
                    if (0x23e < nLevelPrestId)
                    {
                        nLevelPrestId += JungPresetOffset(eLevelId);
                        // recon OutJung.cpp:111 asserts nSpecialPresetCount <= 2 (the engine
                        // guarantees at most 3 special tiles per jungle level).
                        nRand = gaOutJungSpecialPresetRandOffsets[nSeedModulo, nSpecialPresetCount];
                        nSpecialPresetCount += 1;
                    }
                    if (nLevelPrestId != 0) OutPlace.SpawnOutdoorLevelPresetEx(pLevel, nXCell * 4, nYCell * 4, nLevelPrestId, nRand, 0);
                }
            }

            nYCell += 1;
            if (nYCell >= nLevelHeightCells) break;
        }
    }

    /// <summary>OutJung::SpawnRandomPreset   OutJung.cpp:131 (0067eed0). Fisher-Yates shuffles ALL
    /// grid cells (full grid, no interior inset), then stamps a preset (id randomly drawn in [id1,
    /// id2]) at each shuffled cell that fits, up to nLimit placements (0 = unlimited).</summary>
    public static void SpawnRandomPreset(D2DrlgLevelStrc* pLevel, int nLevelPrestId1, int nLevelPrestId2, int nLimit)
    {
        var pData = Wild(pLevel);
        void* pMemory = pLevel->pDrlg->pMemoryPool;
        uint nSwapIdxB = (uint)(nLevelPrestId2 - nLevelPrestId1 + 1);
        int nGridWidthCells = pData->nGridCoordsWidth;
        int nTotalCells = pData->nGridCoordsHeight * nGridWidthCells;
        int nPlacedCount = 0;
        if (nTotalCells == 0) return;

        int* pCells = (int*)Pool.AllocServerMemory(pMemory, (nuint)(nTotalCells * 8));

        // Init: cell[i] = { x = i % width, y = i / width }.
        for (int i = 0; i < nTotalCells; i++)
        {
            pCells[i * 2] = i % nGridWidthCells;
            pCells[i * 2 + 1] = i / nGridWidthCells;
        }

        // Fisher-Yates: two index draws per step, both via the verified selector.
        uint nShuffleCountdown = (uint)nTotalCells;
        while (nShuffleCountdown != 0)
        {
            int a = (int)DrlgRng.RandomNumberSelector(&pLevel->sSeed, (uint)nTotalCells);
            int b = (int)DrlgRng.RandomNumberSelector(&pLevel->sSeed, (uint)nTotalCells);
            nShuffleCountdown -= 1;
            int tx = pCells[a * 2];
            int ty = pCells[a * 2 + 1];
            pCells[a * 2] = pCells[b * 2];
            pCells[a * 2 + 1] = pCells[b * 2 + 1];
            pCells[b * 2] = tx;
            pCells[b * 2 + 1] = ty;
        }

        for (int i = 0; i < nTotalCells; i++)
        {
            int nX = pCells[i * 2];
            int nY = pCells[i * 2 + 1];
            int nRand = (int)DrlgRng.RandomNumberSelector(&pLevel->sSeed, nSwapIdxB);
            if (OutPlace.TestOutdoorLevelPreset(pLevel, nX, nY, nLevelPrestId1 + nRand, 0, 0x0f) != 0)
            {
                OutPlace.SpawnOutdoorLevelPresetEx(pLevel, nX, nY, nLevelPrestId1 + nRand, -1, 0);
                nPlacedCount += 1;
                if (0 < nLimit && nLimit <= nPlacedCount) break;
            }
        }

        Pool.FreeServerMemory(pMemory, pCells);
    }

    /// <summary>DRLGOUTJUNG_BuildKurastDocktownBorders   Drlg.cpp:5828 (0067ead0)</summary>
    private static void BuildKurastDocktownBorders(D2DrlgLevelStrc* pLevel)
    {
        int nX = Div8(pLevel->sCoordinatesAndSize.WorldSize.x) - 1;
        int nY = Div8(pLevel->sCoordinatesAndSize.WorldSize.y) - 1;
        int nPortalX = Div8(pLevel->sCoordinatesAndSize.WorldSize.x);
        if (pLevel->pDrlg->bJungleInterlink == 0) nPortalX -= 2;
        else nPortalX = 1;

        if (1 < nX)
            for (int nXIter = 1; nXIter < nX; nXIter++)
                OutPlace.SpawnOutdoorLevelPresetEx(pLevel, nXIter, 0, (nXIter == nPortalX ? 1 : 0) * 8 + 0x25d, -1, 0);

        if (1 < nX)
        {
            int nYIter = 1;
            while (nYIter < nX)
            {
                OutPlace.SpawnOutdoorLevelPresetEx(pLevel, nYIter, nY, (nYIter == nX / 2 ? 1 : 0) * 8 + 0x25e, -1, 0);
                nYIter += 1 + (nYIter == nX / 2 ? 1 : 0);
            }
        }

        if (1 < nY)
        {
            for (int nYIter = 1; nYIter < nY; nYIter++)
            {
                OutPlace.SpawnOutdoorLevelPresetEx(pLevel, nX, nYIter, 0x25f, -1, 0);
                OutPlace.SpawnOutdoorLevelPresetEx(pLevel, 0, nYIter, 0x260, -1, 0);
            }
        }

        OutPlace.SpawnOutdoorLevelPresetEx(pLevel, 0, 0, 0x262, -1, 0);
        OutPlace.SpawnOutdoorLevelPresetEx(pLevel, nX, 0, 0x261, -1, 0);
        OutPlace.SpawnOutdoorLevelPresetEx(pLevel, 0, nY, 0x264, -1, 0);
        OutPlace.SpawnOutdoorLevelPresetEx(pLevel, nX, nY, 0x263, -1, 0);
    }

    /// <summary>DRLGOUTJUNG_BuildKurastUpperBorders   Drlg.cpp:5872 (0067ec30)</summary>
    private static void BuildKurastUpperBorders(D2DrlgLevelStrc* pLevel)
    {
        int nPortalX2base = Div8(pLevel->sCoordinatesAndSize.WorldSize.x);
        int nY = Div8(pLevel->sCoordinatesAndSize.WorldSize.y) - 1;
        int nX = nPortalX2base - 1;
        int nPortalX1, nPortalX2;
        if (pLevel->pDrlg->bJungleInterlink == 0)
        {
            nPortalX1 = nPortalX2base - 2;
            nPortalX2 = 1;
        }
        else
        {
            nPortalX1 = 1;
            nPortalX2 = nPortalX2base - 2;
        }

        if (1 < nX)
        {
            for (int nIter = 1; nIter < nX; nIter++)
            {
                OutPlace.SpawnOutdoorLevelPresetEx(pLevel, nIter, 0, (nIter == nPortalX2 ? 1 : 0) * 8 + 0x26b, -1, 0);
                OutPlace.SpawnOutdoorLevelPresetEx(pLevel, nIter, nY, (nIter == nPortalX1 ? 1 : 0) * 8 + 0x26c, -1, 0);
            }
        }

        if (1 < nY)
        {
            for (int nIter = 1; nIter < nY; nIter++)
            {
                OutPlace.SpawnOutdoorLevelPresetEx(pLevel, nX, nIter, 0x26d, -1, 0);
                OutPlace.SpawnOutdoorLevelPresetEx(pLevel, 0, nIter, 0x26e, -1, 0);
            }
        }

        OutPlace.SpawnOutdoorLevelPresetEx(pLevel, 0, 0, 0x270, -1, 0);
        OutPlace.SpawnOutdoorLevelPresetEx(pLevel, nX, 0, 0x26f, -1, 0);
        OutPlace.SpawnOutdoorLevelPresetEx(pLevel, 0, nY, 0x272, -1, 0);
        OutPlace.SpawnOutdoorLevelPresetEx(pLevel, nX, nY, 0x271, -1, 0);
    }

    /// <summary>DRLGOUTJUNG_BuildKurastCausewayBorders   Drlg.cpp:5912 (0067ed70)</summary>
    private static void BuildKurastCausewayBorders(D2DrlgLevelStrc* pLevel)
    {
        int nPortalXbase = Div8(pLevel->sCoordinatesAndSize.WorldSize.x);
        int nY = Div8(pLevel->sCoordinatesAndSize.WorldSize.y) - 1;
        int nX = nPortalXbase - 1;
        int nPortalX = pLevel->pDrlg->bJungleInterlink == 0 ? 1 : nPortalXbase - 2;

        if (1 < nX)
        {
            int nIter = 1;
            while (nIter < nX)
            {
                OutPlace.SpawnOutdoorLevelPresetEx(pLevel, nIter, 0, (nIter == nX / 2 ? 1 : 0) * 8 + 0x27c, -1, 0);
                nIter += 1 + (nIter == nX / 2 ? 1 : 0);
            }
        }

        if (1 < nX)
            for (int nIter = 1; nIter < nX; nIter++)
                OutPlace.SpawnOutdoorLevelPresetEx(pLevel, nIter, nY, (nIter == nPortalX ? 1 : 0) * 8 + 0x27d, -1, 0);

        if (1 < nY)
        {
            for (int nIter = 1; nIter < nY; nIter++)
            {
                OutPlace.SpawnOutdoorLevelPresetEx(pLevel, nX, nIter, 0x27e, -1, 0);
                OutPlace.SpawnOutdoorLevelPresetEx(pLevel, 0, nIter, 0x27f, -1, 0);
            }
        }

        OutPlace.SpawnOutdoorLevelPresetEx(pLevel, 0, 0, 0x281, -1, 0);
        OutPlace.SpawnOutdoorLevelPresetEx(pLevel, nX, 0, 0x280, -1, 0);
        OutPlace.SpawnOutdoorLevelPresetEx(pLevel, 0, nY, 0x283, -1, 0);
        OutPlace.SpawnOutdoorLevelPresetEx(pLevel, nX, nY, 0x282, -1, 0);
    }

    /// <summary>GetOutLinkVisFlag   Outdoors.cpp:380 (006755..), Act-III variant: the link-vis
    /// path (pOrthData != null) is the upstream-dependent tail and returns 0, exactly as libd2.</summary>
    private static int GetOutLinkVisFlag(D2DrlgLevelStrc* pLevel, D2DrlgVertexStrc* pVertex)
    {
        var pData = Wild(pLevel);
        if (pData->pOrthData == null) return 0;
        return 0;
    }

    /// <summary>SetOutGridLinkFlags   OutPlace.cpp:268 (00675770), Act-III variant. Walks the
    /// boundary vertex ring; for each edge vertex marks the boundary cells in sGridOutdoor occupied
    /// (nDirection*2+1, an ODD value so bit 0 is set — blocking later TestOutdoorLevelPreset
    /// placements there). No RNG.</summary>
    private static void SetOutGridLinkFlags(D2DrlgLevelStrc* pLevel)
    {
        var pData = Wild(pLevel);
        var pHead = pData->pVertices;
        if (pHead == null) return;
        var pVertex = pHead;
        var pNext = pHead->pNext;
        while (true)
        {
            if ((pVertex->dwFlags & 1) != 0)
            {
                int nLinkVisFlag = GetOutLinkVisFlag(pLevel, pVertex);
                int byDirection = pVertex->nDirection;
                DrlgGrid.SetEdgeGridFlags(&pData->sGridLink, pVertex, nLinkVisFlag, 0, true);
                DrlgGrid.SetEdgeGridFlags(&pData->sGridOutdoor, pVertex, byDirection * 2 + 1, 0, true);
            }
            bool bHasNext = pNext != pHead;
            pVertex = pNext;
            pNext = pNext->pNext;
            if (!bHasNext) break;
        }
    }

    /// <summary>OutPlace::BuildKurast   OutPlace.cpp:1584 (0067f190)</summary>
    private static void BuildKurast(D2DrlgLevelStrc* pLevel)
    {
        switch (pLevel->eD2LevelId)
        {
            case LevelIds.LowerKurast: BuildKurastDocktownBorders(pLevel); break;
            case LevelIds.KurastBazaar: BuildKurastUpperBorders(pLevel); break;
            case LevelIds.UpperKurast: BuildKurastCausewayBorders(pLevel); break;
        }

        int nLevelWidthCells = Div8(pLevel->sCoordinatesAndSize.WorldSize.x) - 4;
        int nLevelHeightCells = Div8(pLevel->sCoordinatesAndSize.WorldSize.y) - 4;
        switch (pLevel->eD2LevelId)
        {
            case LevelIds.LowerKurast:
                OutPlace.SpawnOutdoorLevelPreset(pLevel, 0x277, 0, 0, 0x0f);
                SpawnRandomPreset(pLevel, 0x26a, 0x26a, 4);
                SpawnRandomPreset(pLevel, 0x268, 0x269, 0);
                SpawnRandomPreset(pLevel, 0x267, 0x267, 0);
                break;
            case LevelIds.KurastBazaar:
                OutPlace.SpawnOutdoorLevelPresetEx(pLevel, 3, 3, 0x275, 0, 0);
                OutPlace.SpawnOutdoorLevelPresetEx(pLevel, nLevelWidthCells, 3, 0x275, 1, 0);
                OutPlace.SpawnOutdoorLevelPreset(pLevel, 0x276, 0, 0, 0x0f);
                OutPlace.SpawnOutdoorLevelPreset(pLevel, 0x276, 1, 0, 0x0f);
                OutPlace.SpawnOutdoorLevelPreset(pLevel, 0x277, 0, 0, 0x0f);
                SpawnRandomPreset(pLevel, 0x27b, 0x27b, 4);
                SpawnRandomPreset(pLevel, 0x279, 0x27a, 0);
                SpawnRandomPreset(pLevel, 0x278, 0x278, 0);
                break;
            case LevelIds.UpperKurast:
                OutPlace.SpawnOutdoorLevelPresetEx(pLevel, 3, nLevelHeightCells, 0x286, 0, 0);
                OutPlace.SpawnOutdoorLevelPresetEx(pLevel, nLevelWidthCells, nLevelHeightCells, 0x286, 1, 0);
                OutPlace.SpawnOutdoorLevelPreset(pLevel, 0x287, 0, 0, 0x0f);
                OutPlace.SpawnOutdoorLevelPreset(pLevel, 0x287, 1, 0, 0x0f);
                OutPlace.SpawnOutdoorLevelPreset(pLevel, 0x277, 0, 0, 0x0f);
                SpawnRandomPreset(pLevel, 0x28b, 0x28b, 4);
                SpawnRandomPreset(pLevel, 0x289, 0x28a, 0);
                SpawnRandomPreset(pLevel, 0x288, 0x288, 0);
                break;
            case LevelIds.KurastCauseway:
                OutPlace.SpawnOutdoorLevelPresetEx(pLevel, 0, 0, 0x28c, 0, 0);
                break;
        }
    }

    /// <summary>DRLGLEVEL_FixDrlgLevelForLowerKurast   0x00678910. Called from DRLGActMisc_InitAct3
    /// (0x006789b0) with pLevel = the return of FixDrlgLevelForSpiderForest (the topmost jungle
    /// sub-level = Flayer Jungle, 0x4e). Positions the 5-level Kurast chain (LowerKurast 0x4f ..
    /// Travincal 0x53) as one vertical stack centered on Flayer Jungle's x, climbing upward
    /// (decreasing y) by each level's cumulative SizeY. Coords are in tiles.</summary>
    private static D2DrlgLevelStrc* FixDrlgLevelForLowerKurast(D2DrlgLevelStrc* pLevel)
    {
        var pDrlgActMisc = pLevel->pDrlg;
        int nParentSizeX = pLevel->sCoordinatesAndSize.WorldSize.x;
        int parentX = pLevel->sCoordinatesAndSize.WorldPosition.x;
        int parentY = pLevel->sCoordinatesAndSize.WorldPosition.y;
        int byDifficulty = pDrlgActMisc->nDifficulty;

        int nYOffset = 0;
        D2DrlgLevelStrc* pSubLevel = null;
        int eLevel = LevelIds.LowerKurast; // 0x4f
        int nSubLevelCount = 5;
        while (true)
        {
            var pDefs = DrlgTables.LevelDefsGetLine(eLevel);
            int nSubSizeY = pDefs->SizeY[byDifficulty];
            int nSubSizeX = pDefs->SizeX[byDifficulty];
            nYOffset -= nSubSizeY;
            pSubLevel = Drlg.GetLevelAndAlloc(pDrlgActMisc, eLevel);
            eLevel += LevelIds.RogueEncampment; // +1
            pSubLevel->sCoordinatesAndSize.WorldPosition.x = (nParentSizeX / 2 - nSubSizeX / 2) + parentX;
            pSubLevel->sCoordinatesAndSize.WorldPosition.y = parentY + nYOffset;
            pSubLevel->sCoordinatesAndSize.WorldSize.x = nSubSizeX;
            pSubLevel->sCoordinatesAndSize.WorldSize.y = nSubSizeY;
            nSubLevelCount -= 1;
            if (nSubLevelCount == 0) break;
        }
        return pSubLevel;
    }

    /// <summary>OutPlace::BuildTravincal   OutPlace.cpp:1642 (0067f3b0)</summary>
    private static void BuildTravincal(D2DrlgLevelStrc* pLevel)
    {
        if (pLevel->eD2LevelId != LevelIds.Travincal) return;
        OutPlace.SpawnOutdoorLevelPresetEx(pLevel, 0, 0, 0x28d, -1, 0);
        OutPlace.SpawnOutdoorLevelPresetEx(pLevel, 2, 0, 0x28e, -1, 0);
        OutPlace.SpawnOutdoorLevelPresetEx(pLevel, 6, 0, 0x28f, -1, 0);
        OutPlace.SpawnOutdoorLevelPresetEx(pLevel, 0, 4, 0x290, -1, 0);
        OutPlace.SpawnOutdoorLevelPresetEx(pLevel, 2, 4, 0x291, -1, 0);
        OutPlace.SpawnOutdoorLevelPresetEx(pLevel, 6, 4, 0x292, -1, 0);
    }

    /// <summary>OutPlace::InitAct3OutdoorLevel   OutPlace.cpp:1657 (0067f450). SetOutGridLinkFlags
    /// marks the boundary cells occupied in sGridOutdoor — that matters for Act-III: the shrine
    /// passes (SpawnRandomPreset) must skip the border-preset ring.</summary>
    public static void InitAct3OutdoorLevel(D2DrlgLevelStrc* pLevel)
    {
        // Faithful Act-III interlink roll (Drlg.cpp:1014-1016), recomputed from the stored game
        // seed.
        var sSeed = new D2SeedStrc { nSeedLow = (int)pLevel->pDrlg->dwGameLowSeed, nSeedHigh = 0x29a };
        sSeed = DrlgRng.SeedNext(sSeed); // allocDrlgActMisc:289
        sSeed = DrlgRng.SeedNext(sSeed); // ACT_III branch:314
        pLevel->pDrlg->bJungleInterlink = sSeed.nSeedLow & 1;

        // DRLGActMisc_InitAct3 (0x006789b0) calls FixDrlgLevelForSpiderForest once with
        // pDrlg.nSeed at the Act-III initial state (2 advances from {dwGameLowSeed,0x29a}). The
        // seed has been consumed by earlier level generations, so it is restored here before
        // driving the placement. L75 must already have been InitLevel'd.
        int eLvl = pLevel->eD2LevelId;
        if (eLvl == LevelIds.SpiderForest && pLevel->pAutomapEx == null)
        {
            var pDrlg = pLevel->pDrlg;
            var pL75 = Drlg.GetLevelAndAlloc(pDrlg, LevelIds.KurastDocktown);
            if (pL75->sCoordinatesAndSize.WorldPosition.y != 0)
            {
                var act3Seed = new D2SeedStrc { nSeedLow = (int)pDrlg->dwGameLowSeed, nSeedHigh = 0x29a };
                act3Seed = DrlgRng.SeedNext(act3Seed);
                act3Seed = DrlgRng.SeedNext(act3Seed);
                pDrlg->nSeed = act3Seed;
                // DRLGActMisc_InitAct3 (0x006789b0) chains these: FixSpiderForest returns the
                // topmost jungle (Flayer Jungle), off which FixLowerKurast stacks 0x4f..0x53.
                var pTopJungle = FixDrlgLevelForSpiderForest(pL75);
                if (pTopJungle != null) FixDrlgLevelForLowerKurast(pTopJungle);
            }
        }

        SetOutGridLinkFlags(pLevel);
        BuildJungle(pLevel);
        BuildKurast(pLevel);
        BuildTravincal(pLevel);
    }
}
