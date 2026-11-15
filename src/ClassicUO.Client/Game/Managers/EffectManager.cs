// SPDX-License-Identifier: BSD-2-Clause

using System;
using ClassicUO.Game.Data;
using ClassicUO.Game.GameObjects;
using ClassicUO.Utility.Logging;

namespace ClassicUO.Game.Managers
{
    public sealed class EffectManager : LinkedObject
    {
        private readonly World _world;

        public EffectManager(World world)
        {
            _world = world;
        }

        public void Update()
        {
            for (var f = (GameEffect) Items; f != null;)
            {
                var next = (GameEffect) f.Next;

                f.Update();

                if (!f.IsDestroyed && f.Distance > _world.ClientViewRange)
                {
                    f.Destroy();
                }

                f = next;
            }
        }


        public void CreateEffect
        (
            GraphicEffectType type,
            uint source,
            uint target,
            ushort graphic,
            ushort hue,
            ushort srcX,
            ushort srcY,
            sbyte srcZ,
            ushort targetX,
            ushort targetY,
            sbyte targetZ,
            byte speed,
            int duration,
            bool fixedDir,
            bool doesExplode,
            bool hasparticles,
            GraphicEffectBlendMode blendmode
        )
        {
            // ADVENTURE: a coluna de fogo antiga do Flamestrike (0x3709) vem do proprio Sphere
            // (nao sai pelo sphere_spells). Em vez de mostrar a coluna, acende o CORPO EM CHAMAS novo
            // no char atingido por 2 s. Vale para tudo que manda 0x3709 num char (ex.: armas de fogo).
            if (graphic == 0x3709 && (type == GraphicEffectType.FixedFrom || type == GraphicEffectType.Moving))
            {
                uint advAlvo = type == GraphicEffectType.Moving ? target : source;

                if (SerialHelper.IsMobile(advAlvo) && _world.Mobiles.Get(advAlvo) != null)
                {
                    ClassicUO.Game.UI.Gumps.AdventureFxGump.Receber(_world, 2, advAlvo, 0, 0, 0, 1, 0, 2000);

                    return;
                }
            }

            if (hasparticles)
            {
                Log.Warn("Unhandled particles in an effects packet.");
            }

            GameEffect effect;

            if (hue != 0)
            {
                hue++;
            }

            duration *= Constants.ITEM_EFFECT_ANIMATION_DELAY;

            switch (type)
            {
                case GraphicEffectType.Moving:
                    if (graphic <= 0)
                    {
                        return;
                    }

                    // TODO: speed == 0 means run at standard frameInterval got from anim.mul?
                    if (speed == 0)
                    {
                        speed++;
                    }

                    effect = new MovingEffect
                    (
                        _world,
                        this,
                        source,
                        target,
                        srcX,
                        srcY,
                        srcZ,
                        targetX,
                        targetY,
                        targetZ,
                        graphic,
                        hue,
                        fixedDir,
                        duration,
                        speed
                    )
                    {
                        Blend = blendmode,
                        CanCreateExplosionEffect = doesExplode
                    };

                    break;

                case GraphicEffectType.DragEffect:

                    if (graphic <= 0)
                    {
                        return;
                    }

                    if (speed == 0)
                    {
                        speed++;
                    }

                    effect = new DragEffect
                    (
                        _world,
                        this,
                        source,
                        target,
                        srcX,
                        srcY,
                        srcZ,
                        targetX,
                        targetY,
                        targetZ,
                        graphic,
                        hue,
                        duration,
                        speed
                    )
                    {
                        Blend = blendmode,
                        CanCreateExplosionEffect = doesExplode
                    };

                    break;

                case GraphicEffectType.Lightning:
                    effect = new LightningEffect
                    (
                        _world,
                        this,
                        source,
                        srcX,
                        srcY,
                        srcZ,
                        hue
                    );

                    break;

                case GraphicEffectType.FixedXYZ:

                    if (graphic <= 0)
                    {
                        return;
                    }

                    effect = new FixedEffect
                    (
                        _world,
                        this,
                        srcX,
                        srcY,
                        srcZ,
                        graphic,
                        hue,
                        duration,
                        0 //speed [use 50ms]
                    )
                    {
                        Blend = blendmode
                    };

                    break;

                case GraphicEffectType.FixedFrom:

                    if (graphic <= 0)
                    {
                        return;
                    }

                    effect = new FixedEffect
                    (
                        _world,
                        this,
                        source,
                        srcX,
                        srcY,
                        srcZ,
                        graphic,
                        hue,
                        duration,
                        0 //speed [use 50ms]
                    )
                    {
                        Blend = blendmode
                    };

                    break;

                case GraphicEffectType.ScreenFade:
                    Log.Warn("Unhandled 'Screen Fade' effect.");

                    return;

                default:
                    Log.Warn("Unhandled effect.");

                    return;
            }


            PushToBack(effect);
        }

        public new void Clear()
        {
            var first = (GameEffect) Items;

            while (first != null)
            {
                LinkedObject n = first.Next;

                first.Destroy();

                first = (GameEffect) n;
            }

            Items = null;
        }
    }
}