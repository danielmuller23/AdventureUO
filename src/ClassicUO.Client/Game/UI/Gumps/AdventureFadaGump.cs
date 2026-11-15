// SPDX-License-Identifier: BSD-2-Clause
// ==========================================================================
// ADVENTURE: FADA do player. Desenhada pelo proprio cliente, voando ao lado do
// personagem. Nao existe NPC nem item no mundo.
//  - Bate as asas (a largura da arte vai e volta) e flutua para cima e para baixo.
//  - Ataque: voa ate o mob deixando um rastro de brilho e volta para o dono.
//  - Cura: pisca verde.
//
// Pacote proprio 0xBF sub 0xAD03 (15 bytes):
//    [nivel byte][estado byte][arte word][cor word][alvo dword]
//    nivel 0 = sem fada. estado: 0 normal, 1 curando, 2 atacando (arte+1, voa ate o alvo)
//    arte = numero da fada no art.mul (aba Items). A arte de ataque fica no slot seguinte.
// ==========================================================================

using System;
using ClassicUO.Game.GameObjects;
using ClassicUO.Game.Managers;
using ClassicUO.Game.UI.Controls;
using ClassicUO.Renderer;
using Microsoft.Xna.Framework;

namespace ClassicUO.Game.UI.Gumps
{
    public class AdventureFadaGump : Gump
    {
        private const ushort HUE_CURA = 0x0044;   // verde quando cura
        private const uint TEMPO_CURA = 1200;     // ms piscando verde
        private const uint TEMPO_ATAQUE = 700;    // ms do voo ate o mob e de volta

        private static AdventureFadaGump _instancia;

        private readonly ResizableStaticPic _pic;
        private int _nivel, _tam = 20;
        private uint _arte;
        private ushort _cor;
        private int _estado;
        private uint _inicioEstado, _fimEstado;
        private uint _alvo;

        public AdventureFadaGump(World world) : base(world, 0, 0)
        {
            _instancia = this;
            CanMove = false;
            AcceptMouseInput = false;
            CanCloseWithRightClick = false;
            WantUpdateSize = false;
            Width = 32;
            Height = 32;
            _pic = new ResizableStaticPic(0, 20, 20);
            Add(_pic);
        }

        public static void Receber(World world, int nivel, int estado, ushort arte, ushort cor, uint alvo)
        {
            if (_instancia == null || _instancia.IsDisposed)
            {
                if (nivel <= 0)
                {
                    return;
                }

                UIManager.Add(new AdventureFadaGump(world));
            }

            _instancia.Definir(nivel, estado, arte, cor, alvo);
        }

        private void Definir(int nivel, int estado, ushort arte, ushort cor, uint alvo)
        {
            _nivel = nivel;
            _arte = arte;
            _cor = cor;

            if (estado > 0)
            {
                _estado = estado;
                _alvo = alvo;
                _inicioEstado = Time.Ticks;
                _fimEstado = Time.Ticks + (estado == 2 ? TEMPO_ATAQUE : TEMPO_CURA);
            }

            // Tamanho cresce com o nivel: 20 no nivel 1 ate 32 no nivel 5 (a arte tem 32)
            _tam = 17 + Math.Min(Math.Max(nivel, 1), 5) * 3;
        }

        // Ponto na tela (ja com camera) em cima de um mobile
        private static Point Tela(Mobile m, int dx, int dy)
        {
            int wx = (int)(m.RealScreenPosition.X + m.Offset.X + 22) + dx;
            int wy = (int)(m.RealScreenPosition.Y + (m.Offset.Y - m.Offset.Z)) + dy;
            Point p = Client.Game.Scene.Camera.WorldToScreen(new Point(wx, wy));
            Camera camera = Client.Game.Scene.Camera;

            return new Point(p.X + camera.Bounds.X, p.Y + camera.Bounds.Y);
        }

        public override bool Draw(UltimaBatcher2D batcher, int x, int y)
        {
            Mobile dono = World.Player;

            if (_nivel <= 0 || dono == null || _arte == 0)
            {
                return false;
            }

            if (_estado > 0 && Time.Ticks >= _fimEstado)
            {
                _estado = 0;
            }

            double t = Time.Ticks / 1000.0;

            // Posicao "de casa": ao lado do ombro, flutuando (mais alto se montado)
            int bobY = (int)(Math.Sin(t * 3.0) * 4);
            int bobX = (int)(Math.Cos(t * 1.5) * 3);
            Point casa = Tela(dono, 20 + bobX, -(dono.IsMounted ? 75 : 55) + bobY);
            Point pos = casa;
            bool voando = false;

            // Ataque: vai ate o mob e volta (ida na primeira metade, volta na segunda)
            if (_estado == 2 && _alvo != 0)
            {
                Mobile mob = World.Mobiles.Get(_alvo);

                if (mob != null)
                {
                    Point alvo = Tela(mob, 0, -30);
                    float f = (Time.Ticks - _inicioEstado) / (float)TEMPO_ATAQUE;
                    float ida = f < 0.5f ? f * 2f : (1f - f) * 2f;   // 0 -> 1 -> 0
                    ida = ida * ida * (3 - 2 * ida);                // suaviza o movimento
                    pos = new Point(casa.X + (int)((alvo.X - casa.X) * ida), casa.Y + (int)((alvo.Y - casa.Y) * ida));
                    voando = true;

                    // Rastro de brilho entre o dono e a fada
                    for (int i = 1; i <= 8; i++)
                    {
                        float k = i / 9f;
                        int rx = casa.X + (int)((pos.X - casa.X) * k);
                        int ry = casa.Y + (int)((pos.Y - casa.Y) * k);
                        int s = 2 + i / 3;
                        batcher.Draw(SolidColorTextureCache.GetTexture(new Color(255, 245, 190)), new Rectangle(rx - s / 2, ry - s / 2, s, s), ShaderHueTranslator.GetHueVector(0, false, 0.25f + 0.07f * i));
                    }
                }
            }

            // Bater de asas: a largura vai e volta rapido (mais rapido quando voa para atacar)
            float asa = 0.72f + 0.28f * (float)Math.Abs(Math.Sin(t * (voando ? 22.0 : 12.0)));
            int w = Math.Max(6, (int)(_tam * asa));

            _pic.Graphic = _estado == 2 ? _arte + 1 : _arte;
            _pic.Hue = _estado == 1 ? HUE_CURA : _cor;
            _pic.Width = w;
            _pic.Height = _tam;
            _pic.X = (_tam - w) / 2;
            _pic.Y = 0;

            Width = _tam;
            Height = _tam;
            X = pos.X - (_tam >> 1);
            Y = pos.Y - (_tam >> 1);

            return base.Draw(batcher, X, Y);
        }
    }
}
