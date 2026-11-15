// SPDX-License-Identifier: BSD-2-Clause

using System;

namespace ClassicUO.Game.Data
{
    internal static class MovementSpeed
    {
        public const int STEP_DELAY_MOUNT_RUN = 100;
        public const int STEP_DELAY_MOUNT_WALK = 200;
        public const int STEP_DELAY_RUN = 200;
        public const int STEP_DELAY_WALK = 400;

        // Floor for a rendered step; below this a step reads as a teleport.
        public const int STEP_DELAY_MIN = 50;

        // Arrival gaps outside this band are bursts or pauses, not strides.
        public const int STEP_SAMPLE_MIN = 25;
        public const int STEP_PAUSE_MAX = 1000;

        // A measured cadence slower than this multiple of the walk time renders as
        // step-then-stand: the walk animation runs at a fixed frame rate, so a slower glide
        // slides the feet.
        public const float RENDER_CAP_WALK_MULTIPLIER = 1.5f;

        public static int TimeToCompleteMovement(bool run, bool mounted)
        {
            if (mounted)
            {
                return run ? STEP_DELAY_MOUNT_RUN : STEP_DELAY_MOUNT_WALK;
            }

            return run ? STEP_DELAY_RUN : STEP_DELAY_WALK;
        }

        // ADVENTURE: velocidade do proprio player em % (100 = normal). O servidor muda com o
        // pacote 0xBF subcomando 0xAD01 (ver ExtendedCommand.cs). Ex.: 130 = 30% mais rapido.
        public static int AdventurePercent = 100;

        // ADVENTURE: tempo de 1 passo do proprio player, ja aplicando o AdventurePercent.
        public static int AdventureTime(bool run, bool mounted)
        {
            int t = TimeToCompleteMovement(run, mounted);

            if (AdventurePercent == 100 || AdventurePercent <= 0)
            {
                return t;
            }

            return Math.Max(t * 100 / AdventurePercent, STEP_DELAY_MIN);
        }

        public static void GetPixelOffset(byte dir, ref float x, ref float y, float framesPerTile)
        {
            float step_NESW_D = 44.0f / framesPerTile;
            float step_NESW = 22.0f / framesPerTile;
            int checkX = 22;
            int checkY = 22;

            switch (dir & 7)
            {
                case 0: //W
                {
                    x *= step_NESW;
                    y *= -step_NESW;

                    break;
                }

                case 1: //NW
                {
                    x *= step_NESW_D;
                    checkX = 44;
                    y = 0.0f;

                    break;
                }

                case 2: //N
                {
                    x *= step_NESW;
                    y *= step_NESW;

                    break;
                }

                case 3: //NE
                {
                    x = 0.0f;
                    y *= step_NESW_D;
                    checkY = 44;

                    break;
                }

                case 4: //E
                {
                    x *= -step_NESW;
                    y *= step_NESW;

                    break;
                }

                case 5: //SE
                {
                    x *= -step_NESW_D;
                    checkX = 44;
                    y = 0.0f;

                    break;
                }

                case 6: //S
                {
                    x *= -step_NESW;
                    y *= -step_NESW;

                    break;
                }

                case 7: //SW
                {
                    x = 0.0f;
                    y *= -step_NESW_D;
                    checkY = 44;

                    break;
                }
            }

            if (x < -checkX)
            {
                x = -checkX;
            }
            else if (x > checkX)
            {
                x = checkX;
            }

            if (y < -checkY)
            {
                y = -checkY;
            }
            else if (y > checkY)
            {
                y = checkY;
            }
        }
    }
}