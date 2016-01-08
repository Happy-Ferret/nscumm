﻿//  Author:
//       scemino <scemino74@gmail.com>
//
//  Copyright (c) 2015 
//
//  This program is free software: you can redistribute it and/or modify
//  it under the terms of the GNU General Public License as published by
//  the Free Software Foundation, either version 3 of the License, or
//  (at your option) any later version.
//
//  This program is distributed in the hope that it will be useful,
//  but WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//  GNU General Public License for more details.
//
//  You should have received a copy of the GNU General Public License
//  along with this program.  If not, see <http://www.gnu.org/licenses/>.

using NScumm.Core;
using System;
using NScumm.Core.Common;

namespace NScumm.Sci.Graphics
{
    enum ColorRemappingType
    {
        None = 0,
        ByRange = 1,
        ByPercent = 2
    }

    /// <summary>
    /// Palette class, handles palette operations like changing intensity, setting up the palette, merging different palettes
    /// </summary>
    internal class GfxPalette
    {
        // Special flag implemented by us for optimization in palette merge
        private const int SCI_PALETTE_MATCH_PERFECT = 0x8000;
        public const int SCI_PALETTE_MATCH_COLORMASK = 0xFF;

        private const int SCI_PAL_FORMAT_CONSTANT = 1;
        private const int SCI_PAL_FORMAT_VARIABLE = 0;

        public Palette _sysPalette;

        private GfxScreen _gfxScreen;
        private ResourceManager _resMan;

        private bool _sysPaletteChanged;
        private bool _useMerging;
        private bool _use16bitColorMatch;

        //private PalSchedule[] _schedules;

        private int _palVaryResourceId;
        private Palette _palVaryOriginPalette;
        private Palette _palVaryTargetPalette;
        private short _palVaryStep;
        private short _palVaryStepStop;
        private short _palVaryDirection;
        private ushort _palVaryTicks;
        private int _palVaryPaused;
        private int _palVarySignal;
        private ushort _totalScreenColors;

        private bool _remapOn;
        private ColorRemappingType[] _remappingType = new ColorRemappingType[256];
        private byte[] _remappingByPercent = new byte[256];
        private byte[] _remappingByRange = new byte[256];
        private ushort _remappingPercentToSet;

        private byte[] _macClut;
        private ISystem _system;
        private GfxScreen _screen;

        public ushort TotalColorCount { get { return _totalScreenColors; } }

        public GfxPalette(ISystem system, ResourceManager resMan, GfxScreen screen)
        {
            _system = system;
            _resMan = resMan;
            _gfxScreen = screen;

            _sysPalette = new Palette();
            _sysPalette.timestamp = 0;
            for (var color = 0; color < 256; color++)
            {
                _sysPalette.colors[color] = new Color();
                _sysPalette.colors[color].used = 0;
                _sysPalette.colors[color].r = 0;
                _sysPalette.colors[color].g = 0;
                _sysPalette.colors[color].b = 0;
                _sysPalette.intensity[color] = 100;
                _sysPalette.mapping[color] = (byte)color;
            }
            // Black and white are hardcoded
            _sysPalette.colors[0].used = 1;
            _sysPalette.colors[255].used = 1;
            _sysPalette.colors[255].r = 255;
            _sysPalette.colors[255].g = 255;
            _sysPalette.colors[255].b = 255;

            _sysPaletteChanged = false;

            // Quest for Glory 3 demo, Eco Quest 1 demo, Laura Bow 2 demo, Police Quest
            // 1 vga and all Nick's Picks all use the older palette format and thus are
            // not using the SCI1.1 palette merging (copying over all the colors) but
            // the real merging done in earlier games. If we use the copying over, we
            // will get issues because some views have marked all colors as being used
            // and those will overwrite the current palette in that case
            if (ResourceManager.GetSciVersion() < SciVersion.V1_1)
            {
                _useMerging = true;
                _use16bitColorMatch = true;
            }
            else if (ResourceManager.GetSciVersion() == SciVersion.V1_1)
            {
                // there are some games that use inbetween SCI1.1 interpreter, so we have
                // to detect if the current game is merging or copying
                _useMerging = _resMan.DetectPaletteMergingSci11();
                _use16bitColorMatch = _useMerging;
                // Note: Laura Bow 2 floppy uses the new palette format and is detected
                //        as 8 bit color matching because of that.
            }
            else {
                // SCI32
                _useMerging = false;
                _use16bitColorMatch = false; // not verified that SCI32 uses 8-bit color matching
            }

            PalVaryInit();

            _macClut = null;
            LoadMacIconBarPalette();

# if ENABLE_SCI32
            _clutTable = 0;
#endif

            switch (_resMan.ViewType)
            {
                case ViewType.Ega:
                    _totalScreenColors = 16;
                    break;
                case ViewType.Amiga:
                    _totalScreenColors = 32;
                    break;
                case ViewType.Amiga64:
                    _totalScreenColors = 64;
                    break;
                case ViewType.Vga:
                case ViewType.Vga11:
                    _totalScreenColors = 256;
                    break;
                default:
                    throw new InvalidOperationException("GfxPalette: Unknown view type");
            }

            _remapOn = false;
            ResetRemapping();
        }

        public void Set(Palette newPalette, bool force, bool forceRealMerge = false)
        {
            int systime = _sysPalette.timestamp;

            if (force || newPalette.timestamp != systime)
            {
                // SCI1.1+ doesnt do real merging anymore, but simply copying over the used colors from other palettes
                //  There are some games with inbetween SCI1.1 interpreters, use real merging for them (e.g. laura bow 2 demo)
                if ((forceRealMerge) || (_useMerging))
                    _sysPaletteChanged |= Merge(newPalette, force, forceRealMerge);
                else
                    _sysPaletteChanged |= Insert(newPalette, _sysPalette);

                // Adjust timestamp on newPalette, so it wont get merged/inserted w/o need
                newPalette.timestamp = _sysPalette.timestamp;

                bool updatePalette = _sysPaletteChanged && _screen._picNotValid == 0;

                if (_palVaryResourceId != -1)
                {
                    // Pal-vary currently active, we don't set at any time, but also insert into origin palette
                    Insert(newPalette, _palVaryOriginPalette);
                    PalVaryProcess(0, updatePalette);
                    return;
                }

                if (updatePalette)
                {
                    SetOnScreen();
                    _sysPaletteChanged = false;
                }
            }
        }

        // Processes pal vary updates
        private void PalVaryProcess(int signal, bool setPalette)
        {
            short stepChange = (short)(signal * _palVaryDirection);

            _palVaryStep += stepChange;
            if (stepChange > 0)
            {
                if (_palVaryStep > _palVaryStepStop)
                    _palVaryStep = _palVaryStepStop;
            }
            else {
                if (_palVaryStep < _palVaryStepStop)
                {
                    if (signal != 0)
                        _palVaryStep = _palVaryStepStop;
                }
            }

            // We don't need updates anymore, if we reached end-position
            if (_palVaryStep == _palVaryStepStop)
                PalVaryRemoveTimer();
            if (_palVaryStep == 0)
                _palVaryResourceId = -1;

            // Calculate inbetween palette
            Color inbetween = new Color();
            short color;
            for (int colorNr = 1; colorNr < 255; colorNr++)
            {
                inbetween.used = _sysPalette.colors[colorNr].used;
                color = (short)(_palVaryTargetPalette.colors[colorNr].r - _palVaryOriginPalette.colors[colorNr].r);
                inbetween.r = (byte)(((color * _palVaryStep) / 64) + _palVaryOriginPalette.colors[colorNr].r);
                color = (short)(_palVaryTargetPalette.colors[colorNr].g - _palVaryOriginPalette.colors[colorNr].g);
                inbetween.g = (byte)(((color * _palVaryStep) / 64) + _palVaryOriginPalette.colors[colorNr].g);
                color = (short)(_palVaryTargetPalette.colors[colorNr].b - _palVaryOriginPalette.colors[colorNr].b);
                inbetween.b = (byte)(((color * _palVaryStep) / 64) + _palVaryOriginPalette.colors[colorNr].b);

                if (inbetween == _sysPalette.colors[colorNr])
                {
                    _sysPalette.colors[colorNr] = inbetween;
                    _sysPaletteChanged = true;
                }
            }

            if ((_sysPaletteChanged) && (setPalette) && (_screen._picNotValid == 0))
            {
                SetOnScreen();
                _sysPaletteChanged = false;
            }
        }

        private void PalVaryRemoveTimer()
        {
            throw new NotImplementedException();
        }

        private bool Insert(Palette newPalette, Palette destPalette)
        {
            bool paletteChanged = false;

            for (int i = 1; i < 255; i++)
            {
                if (newPalette.colors[i].used != 0)
                {
                    if ((newPalette.colors[i].r != destPalette.colors[i].r) ||
                        (newPalette.colors[i].g != destPalette.colors[i].g) ||
                        (newPalette.colors[i].b != destPalette.colors[i].b))
                    {
                        destPalette.colors[i].r = newPalette.colors[i].r;
                        destPalette.colors[i].g = newPalette.colors[i].g;
                        destPalette.colors[i].b = newPalette.colors[i].b;
                        paletteChanged = true;
                    }
                    destPalette.colors[i].used = newPalette.colors[i].used;
                    newPalette.mapping[i] = (byte)i;
                }
            }

            // We don't update the timestamp for SCI1.1, it's only updated on kDrawPic calls
            return paletteChanged;
        }

        private bool Merge(Palette newPalette, bool force, bool forceRealMerge)
        {
            ushort res;
            bool paletteChanged = false;

            for (int i = 1; i < 255; i++)
            {
                // skip unused colors
                if (newPalette.colors[i].used == 0)
                    continue;

                // forced palette merging or dest color is not used yet
                if (force || (_sysPalette.colors[i].used == 0))
                {
                    _sysPalette.colors[i].used = newPalette.colors[i].used;
                    if ((newPalette.colors[i].r != _sysPalette.colors[i].r) ||
                        (newPalette.colors[i].g != _sysPalette.colors[i].g) ||
                        (newPalette.colors[i].b != _sysPalette.colors[i].b))
                    {
                        _sysPalette.colors[i].r = newPalette.colors[i].r;
                        _sysPalette.colors[i].g = newPalette.colors[i].g;
                        _sysPalette.colors[i].b = newPalette.colors[i].b;
                        paletteChanged = true;
                    }
                    newPalette.mapping[i] = (byte)i;
                    continue;
                }

                // is the same color already at the same position? . match it directly w/o lookup
                //  this fixes games like lsl1demo/sq5 where the same rgb color exists multiple times and where we would
                //  otherwise match the wrong one (which would result into the pixels affected (or not) by palette changes)
                if ((_sysPalette.colors[i].r == newPalette.colors[i].r) &&
                    (_sysPalette.colors[i].g == newPalette.colors[i].g) &&
                    (_sysPalette.colors[i].b == newPalette.colors[i].b))
                {
                    newPalette.mapping[i] = (byte)i;
                    continue;
                }

                // check if exact color could be matched
                res = MatchColor(newPalette.colors[i].r, newPalette.colors[i].g, newPalette.colors[i].b);
                if ((res & SCI_PALETTE_MATCH_PERFECT) != 0)
                { // exact match was found
                    newPalette.mapping[i] = (byte)(res & SCI_PALETTE_MATCH_COLORMASK);
                    continue;
                }

                int j = 1;

                // no exact match - see if there is an unused color
                for (; j < 256; j++)
                {
                    if (_sysPalette.colors[j].used == 0)
                    {
                        _sysPalette.colors[j].used = newPalette.colors[i].used;
                        _sysPalette.colors[j].r = newPalette.colors[i].r;
                        _sysPalette.colors[j].g = newPalette.colors[i].g;
                        _sysPalette.colors[j].b = newPalette.colors[i].b;
                        newPalette.mapping[i] = (byte)j;
                        paletteChanged = true;
                        break;
                    }
                }

                // if still no luck - set an approximate color
                if (j == 256)
                {
                    newPalette.mapping[i] = (byte)(res & SCI_PALETTE_MATCH_COLORMASK);
                    _sysPalette.colors[res & SCI_PALETTE_MATCH_COLORMASK].used |= 0x10;
                }
            }

            if (!forceRealMerge)
                _sysPalette.timestamp = Environment.TickCount * 60 / 1000;

            return paletteChanged;
        }

        public void PalVaryPrepareForTransition()
        {
            if (_palVaryResourceId != -1)
            {
                // Before doing transitions, we have to prepare palette
                PalVaryProcess(0, false);
            }
        }

        // This is called for SCI1.1, when kDrawPic got done. We update sysPalette timestamp this way for SCI1.1 and also load
        //  target-palette, if palvary is active
        public void DrewPicture(int pictureId)
        {
            if (!_useMerging) // Don't do this on inbetween SCI1.1 games
                _sysPalette.timestamp++;

            if (_palVaryResourceId != -1)
            {
                if (SciEngine.Instance.EngineState.gameIsRestarting == 0) // only if not restored nor restarted
                    PalVaryLoadTargetPalette(pictureId);
            }
        }

        private bool PalVaryLoadTargetPalette(int resourceId)
        {
            _palVaryResourceId = (resourceId != 65535) ? resourceId : -1;
            var palResource = _resMan.FindResource(new ResourceId(ResourceType.Palette, (ushort)resourceId), false);
            if (palResource!=null)
            {
                // Load and initialize destination palette
                _palVaryTargetPalette = CreateFromData(new ByteAccess(palResource.data), palResource.size);
                return true;
            }
            return false;
        }

        // Actually do the pal vary processing
        public void PalVaryUpdate()
        {
            if (_palVarySignal!=0)
            {
                PalVaryProcess(_palVarySignal, true);
                _palVarySignal = 0;
            }
        }

        public Palette CreateFromData(ByteAccess data, int bytesLeft)
        {
            int palFormat = 0;
            int palOffset = 0;
            int palColorStart = 0;
            int palColorCount = 0;
            int colorNo = 0;
            Palette paletteOut = new Palette();

            // Setup 1:1 mapping
            for (colorNo = 0; colorNo < 256; colorNo++)
            {
                paletteOut.mapping[colorNo] = (byte)colorNo;
            }

            if (bytesLeft < 37)
            {
                // This happens when loading palette of picture 0 in sq5 - the resource is broken and doesn't contain a full
                //  palette
                // TODO: debugC(kDebugLevelResMan, "GfxPalette::createFromData() - not enough bytes in resource (%d), expected palette header", bytesLeft);
                return paletteOut;
            }

            // palette formats in here are not really version exclusive, we can not use sci-version to differentiate between them
            //  they were just called that way, because they started appearing in sci1.1 for example
            if ((data[0] == 0 && data[1] == 1) || (data[0] == 0 && data[1] == 0 && data.Data.ReadSci11EndianUInt16(data.Offset + 29) == 0))
            {
                // SCI0/SCI1 palette
                palFormat = SCI_PAL_FORMAT_VARIABLE; // CONSTANT;
                palOffset = 260;
                palColorStart = 0; palColorCount = 256;
                //memcpy(&paletteOut.mapping, data, 256);
            }
            else {
                // SCI1.1 palette
                palFormat = data[32];
                palOffset = 37;
                palColorStart = data[25];
                palColorCount = data.Data.ReadSci11EndianUInt16(data.Offset + 29);
            }

            switch (palFormat)
            {
                case SCI_PAL_FORMAT_CONSTANT:
                    // Check, if enough bytes left
                    if (bytesLeft < palOffset + (3 * palColorCount))
                    {
                        // TODO: warning("GfxPalette::createFromData() - not enough bytes in resource, expected palette colors");
                        return paletteOut;
                    }

                    for (colorNo = palColorStart; colorNo < palColorStart + palColorCount; colorNo++)
                    {
                        paletteOut.colors[colorNo].used = 1;
                        paletteOut.colors[colorNo].r = data[palOffset++];
                        paletteOut.colors[colorNo].g = data[palOffset++];
                        paletteOut.colors[colorNo].b = data[palOffset++];
                    }
                    break;
                case SCI_PAL_FORMAT_VARIABLE:
                    if (bytesLeft < palOffset + (4 * palColorCount))
                    {
                        // TODO: warning("GfxPalette::createFromData() - not enough bytes in resource, expected palette colors");
                        return paletteOut;
                    }

                    for (colorNo = palColorStart; colorNo < palColorStart + palColorCount; colorNo++)
                    {
                        paletteOut.colors[colorNo].used = data[palOffset++];
                        paletteOut.colors[colorNo].r = data[palOffset++];
                        paletteOut.colors[colorNo].g = data[palOffset++];
                        paletteOut.colors[colorNo].b = data[palOffset++];
                    }
                    break;
            }
            return paletteOut;
        }

        public byte RemapColor(byte remappedColor, byte screenColor)
        {
            //assert(_remapOn);
            if (_remappingType[remappedColor] == ColorRemappingType.ByRange)
                return _remappingByRange[screenColor];
            else if (_remappingType[remappedColor] == ColorRemappingType.ByPercent)
                return _remappingByPercent[screenColor];
            else
                throw new InvalidOperationException("remapColor(): Color {remappedColor} isn't remapped");

            return 0;	// should never reach here
        }

        public bool IsRemapped(byte color)
        {
            return _remapOn && (_remappingType[color] != ColorRemappingType.None);
        }

        private void PalVaryInit()
        {
            _palVaryResourceId = -1;
            _palVaryPaused = 0;
            _palVarySignal = 0;
            _palVaryStep = 0;
            _palVaryStepStop = 0;
            _palVaryDirection = 0;
            _palVaryTicks = 0;
        }

        private void ResetRemapping()
        {
            _remapOn = false;
            _remappingPercentToSet = 0;

            for (int i = 0; i < 256; i++)
            {
                _remappingType[i] = ColorRemappingType.None;
                _remappingByPercent[i] = (byte)i;
                _remappingByRange[i] = (byte)i;
            }
        }

        private void LoadMacIconBarPalette()
        {
            if (!SciEngine.Instance.HasMacIconBar)
                return;

            throw new NotImplementedException();
            //var clutStream = SciEngine.Instance.MacExecutable.GetResource(ScummHelper.MakeTag('c', 'l', 'u', 't'), 150);

            //if (!clutStream)
            //    error("Could not find clut 150 for the Mac icon bar");

            //clutStream.readUint32BE(); // seed
            //clutStream.readUint16BE(); // flags
            //uint16 colorCount = clutStream.readUint16BE() + 1;
            //assert(colorCount == 256);

            //_macClut = new byte[256 * 3];

            //for (uint16 i = 0; i < colorCount; i++)
            //{
            //    clutStream.readUint16BE();
            //    _macClut[i * 3] = clutStream.readUint16BE() >> 8;
            //    _macClut[i * 3 + 1] = clutStream.readUint16BE() >> 8;
            //    _macClut[i * 3 + 2] = clutStream.readUint16BE() >> 8;
            //}

            //// Adjust bounds on the KQ6 palette
            //// We don't use all of it for the icon bar
            //if (g_sci.getGameId() == GID_KQ6)
            //    memset(_macClut + 32 * 3, 0, (256 - 32) * 3);

            //// Force black/white
            //_macClut[0x00 * 3] = 0;
            //_macClut[0x00 * 3 + 1] = 0;
            //_macClut[0x00 * 3 + 2] = 0;
            //_macClut[0xff * 3] = 0xff;
            //_macClut[0xff * 3 + 1] = 0xff;
            //_macClut[0xff * 3 + 2] = 0xff;

            //delete clutStream;
        }

        // meant to get called only once during init of engine
        public void SetDefault()
        {
            if (_resMan.ViewType == ViewType.Ega)
                SetEGA();
            else if (_resMan.ViewType == ViewType.Amiga || _resMan.ViewType == ViewType.Amiga64)
                SetAmiga();
            else
                KernelSetFromResource(999, true);
        }

        private void KernelSetFromResource(int v1, bool v2)
        {
            throw new NotImplementedException();
        }

        private void SetAmiga()
        {
            throw new NotImplementedException();
        }

        private void SetEGA()
        {
            int curColor;
            byte color1, color2;

            _sysPalette.colors[1].r = 0x000; _sysPalette.colors[1].g = 0x000; _sysPalette.colors[1].b = 0x0AA;
            _sysPalette.colors[2].r = 0x000; _sysPalette.colors[2].g = 0x0AA; _sysPalette.colors[2].b = 0x000;
            _sysPalette.colors[3].r = 0x000; _sysPalette.colors[3].g = 0x0AA; _sysPalette.colors[3].b = 0x0AA;
            _sysPalette.colors[4].r = 0x0AA; _sysPalette.colors[4].g = 0x000; _sysPalette.colors[4].b = 0x000;
            _sysPalette.colors[5].r = 0x0AA; _sysPalette.colors[5].g = 0x000; _sysPalette.colors[5].b = 0x0AA;
            _sysPalette.colors[6].r = 0x0AA; _sysPalette.colors[6].g = 0x055; _sysPalette.colors[6].b = 0x000;
            _sysPalette.colors[7].r = 0x0AA; _sysPalette.colors[7].g = 0x0AA; _sysPalette.colors[7].b = 0x0AA;
            _sysPalette.colors[8].r = 0x055; _sysPalette.colors[8].g = 0x055; _sysPalette.colors[8].b = 0x055;
            _sysPalette.colors[9].r = 0x055; _sysPalette.colors[9].g = 0x055; _sysPalette.colors[9].b = 0x0FF;
            _sysPalette.colors[10].r = 0x055; _sysPalette.colors[10].g = 0x0FF; _sysPalette.colors[10].b = 0x055;
            _sysPalette.colors[11].r = 0x055; _sysPalette.colors[11].g = 0x0FF; _sysPalette.colors[11].b = 0x0FF;
            _sysPalette.colors[12].r = 0x0FF; _sysPalette.colors[12].g = 0x055; _sysPalette.colors[12].b = 0x055;
            _sysPalette.colors[13].r = 0x0FF; _sysPalette.colors[13].g = 0x055; _sysPalette.colors[13].b = 0x0FF;
            _sysPalette.colors[14].r = 0x0FF; _sysPalette.colors[14].g = 0x0FF; _sysPalette.colors[14].b = 0x055;
            _sysPalette.colors[15].r = 0x0FF; _sysPalette.colors[15].g = 0x0FF; _sysPalette.colors[15].b = 0x0FF;
            for (curColor = 0; curColor <= 15; curColor++)
            {
                _sysPalette.colors[curColor].used = 1;
            }
            // Now setting colors 16-254 to the correct mix colors that occur when not doing a dithering run on
            //  finished pictures
            for (curColor = 0x10; curColor <= 0xFE; curColor++)
            {
                _sysPalette.colors[curColor].used = 1;
                color1 = (byte)(curColor & 0x0F); color2 = (byte)(curColor >> 4);

                _sysPalette.colors[curColor].r = BlendColors(_sysPalette.colors[color1].r, _sysPalette.colors[color2].r);
                _sysPalette.colors[curColor].g = BlendColors(_sysPalette.colors[color1].g, _sysPalette.colors[color2].g);
                _sysPalette.colors[curColor].b = BlendColors(_sysPalette.colors[color1].b, _sysPalette.colors[color2].b);
            }
            _sysPalette.timestamp = 1;
            SetOnScreen();
        }

        private static byte BlendColors(byte c1, byte c2)
        {
            // linear
            // return (c1/2+c2/2)+((c1&1)+(c2&1))/2;

            // gamma 2.2
            double t = (Math.Pow(c1 / 255.0, 2.2 / 1.0) * 255.0) +
                       (Math.Pow(c2 / 255.0, 2.2 / 1.0) * 255.0);
            return (byte)(0.5 + (Math.Pow(0.5 * t / 255.0, 1.0 / 2.2) * 255.0));
        }

        internal void ModifyAmigaPalette(ByteAccess byteAccess)
        {
            throw new NotImplementedException();
        }

        public void SetOnScreen()
        {
            CopySysPaletteToScreen();
        }

        private void CopySysPaletteToScreen()
        {
            // Get current palette, update it and put back
            var bpal = _system.GraphicsManager.GetPalette();

            for (var i = 0; i < 256; i++)
            {
                if (ColorIsFromMacClut(i))
                {
                    // If we've got a Mac CLUT, override the SCI palette with its non-black colors
                    bpal[i] = Core.Graphics.Color.FromRgb(ConvertMacGammaToSCIGamma(_macClut[i * 3]), ConvertMacGammaToSCIGamma(_macClut[i * 3 + 1]), ConvertMacGammaToSCIGamma(_macClut[i * 3 + 2]));
                }
                else if (_sysPalette.colors[i].used != 0)
                {
                    // Otherwise, copy to the screen
                    bpal[i] = Core.Graphics.Color.FromRgb((byte)ScummHelper.Clip(_sysPalette.colors[i].r * _sysPalette.intensity[i] / 100, 0, 255),
                        (byte)ScummHelper.Clip(_sysPalette.colors[i].g * _sysPalette.intensity[i] / 100, 0, 255),
                        (byte)ScummHelper.Clip(_sysPalette.colors[i].b * _sysPalette.intensity[i] / 100, 0, 255));
                }
            }

            // Check if we need to reset remapping by percent with the new colors.
            if (_remappingPercentToSet != 0)
            {
                for (int i = 0; i < 256; i++)
                {
                    byte r = (byte)(_sysPalette.colors[i].r * _remappingPercentToSet / 100);
                    byte g = (byte)(_sysPalette.colors[i].g * _remappingPercentToSet / 100);
                    byte b = (byte)(_sysPalette.colors[i].b * _remappingPercentToSet / 100);
                    _remappingByPercent[i] = (byte)KernelFindColor(r, g, b);
                }
            }

            _system.GraphicsManager.SetPalette(bpal, 0, 256);
        }

        private short KernelFindColor(byte r, byte g, byte b)
        {
            return (short)(MatchColor(r, g, b) & SCI_PALETTE_MATCH_COLORMASK);
        }

        public ushort MatchColor(byte matchRed, byte matchGreen, byte matchBlue)
        {
            short colorNr;
            short differenceRed, differenceGreen, differenceBlue;
            short differenceTotal = 0;
            short bestDifference = 0x7FFF;
            ushort bestColorNr = 255;

            if (_use16bitColorMatch)
            {
                // used by SCI0 to SCI1, also by the first few SCI1.1 games
                for (colorNr = 0; colorNr < 256; colorNr++)
                {
                    if (_sysPalette.colors[colorNr].used == 0)
                        continue;
                    differenceRed = (short)Math.Abs(_sysPalette.colors[colorNr].r - matchRed);
                    differenceGreen = (short)Math.Abs(_sysPalette.colors[colorNr].g - matchGreen);
                    differenceBlue = (short)Math.Abs(_sysPalette.colors[colorNr].b - matchBlue);
                    differenceTotal = (short)(differenceRed + differenceGreen + differenceBlue);
                    if (differenceTotal <= bestDifference)
                    {
                        bestDifference = differenceTotal;
                        bestColorNr = (ushort)colorNr;
                    }
                }
            }
            else {
                // SCI1.1, starting with QfG3 introduced a bug in the matching code
                // we have to implement it as well, otherwise some colors will be "wrong" in comparison to the original interpreter
                //  See Space Quest 5 bug #6455
                for (colorNr = 0; colorNr < 256; colorNr++)
                {
                    if (_sysPalette.colors[colorNr].used == 0)
                        continue;
                    differenceRed = (byte)Math.Abs((sbyte)(_sysPalette.colors[colorNr].r - matchRed));
                    differenceGreen = (byte)Math.Abs((sbyte)(_sysPalette.colors[colorNr].g - matchGreen));
                    differenceBlue = (byte)Math.Abs((sbyte)(_sysPalette.colors[colorNr].b - matchBlue));
                    differenceTotal = (short)(differenceRed + differenceGreen + differenceBlue);
                    if (differenceTotal <= bestDifference)
                    {
                        bestDifference = differenceTotal;
                        bestColorNr = (ushort)colorNr;
                    }
                }
            }
            if (differenceTotal == 0) // original interpreter does not do this, instead it does 2 calls for merges in the worst case
                return (ushort)(bestColorNr | SCI_PALETTE_MATCH_PERFECT); // we set this flag, so that we can optimize during palette merge
            return bestColorNr;
        }

        private bool ColorIsFromMacClut(int index)
        {
            return index != 0 && _macClut != null && (_macClut[index * 3] != 0 || _macClut[index * 3 + 1] != 0 || _macClut[index * 3 + 2] != 0);
        }

        private static byte ConvertMacGammaToSCIGamma(int comp)
        {
            return (byte)Math.Sqrt(comp * 255.0f);
        }
    }
}
