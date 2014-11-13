﻿//
//  ScummEngine6_Actor.cs
//
//  Author:
//       scemino <scemino74@gmail.com>
//
//  Copyright (c) 2014 
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
using NScumm.Core.Graphics;
using System;
using System.Diagnostics;

namespace NScumm.Core
{
    partial class ScummEngine6
    {
        int _curActor;

        [OpCode(0x7d)]
        void WalkActorToObj(int index, int obj, int dist)
        {
            var a = _actors[index];

            if (obj >= _actors.Length)
            {
                var wio = GetWhereIsObject(obj);

                if (wio != WhereIsObject.FLObject && wio != WhereIsObject.Room)
                    return;

                int dir;
                Point pos;
                GetObjectXYPos(obj, out pos, out dir);
                a.StartWalk(pos, dir);
            }
            else
            {
                var a2 = _actors[obj];
                if (Game.Id == "samnmax" && a2 == null)
                {
                    // WORKAROUND bug #742676 SAM: Fish Farm. Note quite sure why it
                    // happens, whether it's normal or due to a bug in the ScummVM code.
                    Debug.WriteLine("WalkActorToObj: invalid actor {0}", obj);
                    return;
                }
                if (!a.IsInCurrentRoom || !a2.IsInCurrentRoom)
                    return;
                if (dist == 0)
                {
                    dist = (int)(a2.ScaleX * a2.Width / 0xFF);
                    dist += dist / 2;
                }
                var x = a2.Position.X;
                var y = a2.Position.Y;
                if (x < a.Position.X)
                    x += (short)dist;
                else
                    x -= (short)dist;
                a.StartWalk(new Point(x, y), -1);
            }
        }

        [OpCode(0x7e)]
        void WalkActorTo(int index, short x, short y)
        {
            _actors[index].StartWalk(new Point(x, y), -1);
        }

        [OpCode(0x7f)]
        void PutActorAtXY(int actorIndex, short x, short y, int room)
        {
            var actor = _actors[actorIndex];
            if (room == 0xFF || room == 0x7FFFFFFF)
            {
                room = actor.Room;
            }
            else
            {
                if (actor.IsVisible && CurrentRoom != room && TalkingActor == actor.Number)
                {
                    StopTalk();
                }
                if (room != 0)
                {
                    actor.Room = (byte)room;
                }
            }
            actor.PutActor(new Point(x, y), (byte)room);
        }

        [OpCode(0x80)]
        void PutActorAtObject(int index, int obj, byte room)
        {
            var a = _actors[index];
            Point p;
            if (GetWhereIsObject(obj) != WhereIsObject.NotFound)
            {
                p = GetObjectXYPos(obj);
            }
            else
            {
                p = new Point(160, 120);
            }
            if (room == 0xFF)
                room = a.Room;
            a.PutActor(p, room);
        }

        [OpCode(0x81)]
        void FaceActor(int index, int obj)
        {
            _actors[index].FaceToObject(obj);
        }

        [OpCode(0x82)]
        void AnimateActor(int index, int anim)
        {
            _actors[index].Animate(anim);
        }

        [OpCode(0x84)]
        void PickupObject(int obj, byte room)
        {
            if (room == 0)
                room = _roomResource;

            for (var i = 0; i < _inventory.Length; i++)
            {
                if (_inventory[i] == obj)
                {
                    PutOwner(obj, (byte)Variables[VariableEgo.Value]);
                    RunInventoryScript(obj);
                    return;
                }
            }

            AddObjectToInventory(obj, room);
            PutOwner(obj, (byte)Variables[VariableEgo.Value]);
            PutClass(obj, (int)ObjectClass.Untouchable, true);
            PutState(obj, 1);
            MarkObjectRectAsDirty(obj);
            ClearDrawObjectQueue();
            RunInventoryScript(obj);
        }

        [OpCode(0x8a)]
        void GetActorMoving(int index)
        {
            var actor = _actors[index];
            Push((int)actor.Moving);
        }

        [OpCode(0x90)]
        void GetActorWalkBox(int index)
        {
            var actor = _actors[index];
            Push(actor.IgnoreBoxes ? 0 : actor.Walkbox);
        }

        [OpCode(0x91)]
        void GetActorCostume(int index)
        {
            var actor = _actors[index];
            Push(actor.Costume);
        }

        [OpCode(0x9d)]
        void ActorOps()
        {
            var subOp = ReadByte();
            if (subOp == 197)
            {
                _curActor = Pop();
                return;
            }

            var a = _actors[_curActor];
            if (a == null)
                return;

            switch (subOp)
            {
                case 76:                // SO_COSTUME
                    a.SetActorCostume((ushort)Pop());
                    break;
                case 77:                // SO_STEP_DIST
                    {
                        var j = (uint)Pop();
                        var i = (uint)Pop();
                        a.SetActorWalkSpeed(i, j);
                    }
                    break;
                case 78:                // SO_SOUND
                    {
                        var args = GetStackList(8);
                        for (var i = 0; i < args.Length; i++)
                            a.Sounds[i] = (ushort)args[i];
                        break;
                    }
                case 79:                // SO_WALK_ANIMATION
                    a.WalkFrame = (byte)Pop();
                    break;
                case 80:                // SO_TALK_ANIMATION
                    a.TalkStopFrame = (byte)Pop();
                    a.TalkStartFrame = (byte)Pop();
                    break;
                case 81:                // SO_STAND_ANIMATION
                    a.StandFrame = (byte)Pop();
                    break;
                case 82:                // SO_ANIMATION
                    // dummy case in scumm6
                    Pop();
                    Pop();
                    Pop();
                    break;
                case 83:                // SO_DEFAULT
                    a.Init(0);
                    break;
                case 84:                // SO_ELEVATION
                    a.Elevation = Pop();
                    break;
                case 85:                // SO_ANIMATION_DEFAULT
                    a.InitFrame = 1;
                    a.WalkFrame = 2;
                    a.StandFrame = 3;
                    a.TalkStartFrame = 4;
                    a.TalkStopFrame = 5;
                    break;
                case 86:                // SO_PALETTE
                    {
                        var j = (ushort)Pop();
                        var i = Pop();
                        ScummHelper.AssertRange(0, i, 255, "o6_actorOps: palette slot");
                        a.SetPalette(i, j);
                    }
                    break;
                case 87:                // SO_TALK_COLOR
                    a.TalkColor = (byte)Pop();
                    break;
                case 88:                // SO_ACTOR_NAME
                    a.Name = ReadCharacters();
                    break;
                case 89:                // SO_INIT_ANIMATION
                    a.InitFrame = (byte)Pop();
                    break;
                case 91:                // SO_ACTOR_WIDTH
                    a.Width = (uint)Pop();
                    break;
                case 92:                // SO_SCALE
                    {
                        var i = Pop();
                        a.SetScale(i, i);
                    }
                    break;
                case 93:                // SO_NEVER_ZCLIP
                    a.ForceClip = 0;
                    break;
                case 225:               // SO_ALWAYS_ZCLIP
                case 94:                // SO_ALWAYS_ZCLIP
                    a.ForceClip = (byte)Pop();
                    break;
                case 95:                // SO_IGNORE_BOXES
                    a.IgnoreBoxes = true;
                    a.ForceClip = (Game.Version >= 7) ? (byte)100 : (byte)0;
                    if (a.IsInCurrentRoom)
                        a.PutActor();
                    break;
                case 96:                // SO_FOLLOW_BOXES
                    a.IgnoreBoxes = false;
                    a.ForceClip = (Game.Version >= 7) ? (byte)100 : (byte)0;
                    if (a.IsInCurrentRoom)
                        a.PutActor();
                    break;
                case 97:                // SO_ANIMATION_SPEED
                    a.SetAnimSpeed((byte)Pop());
                    break;
                case 98:                // SO_SHADOW
                    a.ShadowMode = (byte)Pop();
                    break;
                case 99:                // SO_TEXT_OFFSET
                    {
                        var y = (short)Pop();
                        var x = (short)Pop();
                        a.TalkPosition = new Point(x, y);
                    }
                    break;
                case 198:               // SO_ACTOR_VARIABLE
                    {
                        var i = Pop();
                        a.SetAnimVar(Pop(), i);
                    }
                    break;
                case 215:               // SO_ACTOR_IGNORE_TURNS_ON
                    a.IgnoreTurns = true;
                    break;
                case 216:               // SO_ACTOR_IGNORE_TURNS_OFF
                    a.IgnoreTurns = false;
                    break;
                case 217:               // SO_ACTOR_NEW
                    a.Init(2);
                    break;
                case 227:               // SO_ACTOR_DEPTH
                    a.Layer = Pop();
                    break;
                case 228:               // SO_ACTOR_WALK_SCRIPT
                    a.WalkScript = (ushort)Pop();
                    break;
                case 229:               // SO_ACTOR_STOP
                    a.StopActorMoving();
                    a.StartAnimActor(a.StandFrame);
                    break;
                case 230:                                                                               /* set direction */
                    a.Moving &= ~MoveFlags.Turn;
                    a.SetDirection(Pop());
                    break;
                case 231:                                                                               /* turn to direction */
                    a.TurnToDirection(Pop());
                    break;
                case 233:               // SO_ACTOR_WALK_PAUSE
                    a.Moving |= MoveFlags.Frozen;
                    break;
                case 234:               // SO_ACTOR_WALK_RESUME
                    a.Moving &= ~MoveFlags.Frozen;
                    break;
                case 235:               // SO_ACTOR_TALK_SCRIPT
                    a.TalkScript = (ushort)Pop();
                    break;
                default:
                    throw new NotSupportedException(string.Format("ActorOps: default case {0}", subOp));
            }
        }

        [OpCode(0x9f)]
        void GetActorFromXY(int x, int y)
        {
            Push(GetActorFromPos(new Point((short)x, (short)y)));
        }

        [OpCode(0xa2)]
        void GetActorElevation(int index)
        {
            var actor = _actors[index];
            Push(actor.Elevation);
        }

        [OpCode(0xa8)]
        void GetActorWidth(int index)
        {
            var actor = _actors[index];
            Push((int)actor.Width);
        }

        [OpCode(0xaa)]
        void GetActorScaleX(int index)
        {
            var actor = _actors[index];
            Push(actor.ScaleX);
        }

        [OpCode(0xab)]
        void GetActorAnimCounter(int index)
        {
            var actor = _actors[index];
            Push(actor.Cost.AnimCounter);
        }

        [OpCode(0xaf)]
        void IsActorInBox(int index, int box)
        {
            var actor = _actors[index];
            Push(CheckXYInBoxBounds(box, actor.Position));
        }

        [OpCode(0xba)]
        void TalkActor(int actor)
        {
            _actorToPrintStrFor = actor;

            // WORKAROUND for bug #2016521: "DOTT: Bernard impersonating LaVerne"
            // Original script did not check for VAR_EGO == 2 before executing
            // a talkActor opcode.
//            if (_game.id == GID_TENTACLE && vm.slot[_currentScript].number == 307
//                && VAR(VAR_EGO) != 2 && _actorToPrintStrFor == 2) {
//                    _scriptPointer += resStrLen(_scriptPointer) + 1;
//                    return;
//                }

            _string[0].LoadDefault();
            ActorTalk(ReadCharacters());
        }

        [OpCode(0xbb)]
        void TalkEgo()
        {
            TalkActor(Variables[VariableEgo.Value]);
        }

        [OpCode(0xd1)]
        void StopTalking()
        {
            StopTalk();
        }

        [OpCode(0xd2)]
        void GetAnimateVariable(int index, int variable)
        {
            var a = _actors[index];
            Push(a.GetAnimVar(variable));
        }

        [OpCode(0xec)]
        void GetActorLayer(int index)
        {
            var actor = _actors[index];
            Push(actor.Layer);
        }
    }
}

