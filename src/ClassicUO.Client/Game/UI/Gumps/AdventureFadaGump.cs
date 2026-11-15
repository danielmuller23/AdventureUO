// SPDX-License-Identifier: BSD-2-Clause
// ==========================================================================
// ADVENTURE: FADA do player. Desenhada pelo proprio cliente, voando ao lado do
// personagem. Nao existe NPC nem item no mundo.
//  - Bate as asas (a largura da arte vai e volta) e flutua para cima e para baixo.
//  - Ataque: voa ate o mob deixando um rastro de brilho e volta para o dono.
//  - Cura: pisca verde.
//  - Exploracao: voa ate o item que achou (t_crica, bau, passagem), fica brilhando e volta.
//  - Cada player tem a sua fada, e os outros players tambem veem (uma fada por dono).
//
// Pacote proprio 0xBF sub 0xAD03 (15 bytes):
//    [nivel byte][estado byte][arte word][cor word][alvo dword]
//    nivel 0 = sem fada. estado: 0 normal, 1 curando, 2 atacando (arte+1, voa ate o alvo),
//    3 explorando (voa ate o ITEM do alvo e fica brilhando em cima dele)
//    (AD03 = fada do proprio player)
// Pacote 0xBF sub 0xAD05 (19 bytes) = fada de QUALQUER player (os outros veem):
//    [dono dword][nivel][estado][arte word][cor word][alvo dword]
//    Sphere: SENDPACKET 0bf w19 w0AD05 d<dono> <nivel> <estado> w<arte> w<cor> d<alvo>
//    arte = numero da fada no art.mul (aba Items). A arte de ataque fica no slot seguinte.
// ==========================================================================

using System;
using System.Collections.Generic;
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
        private const uint TEMPO_EXPLORA = 3000;  // ms: vai ate o item, fica brilhando e volta

        private static readonly Dictionary<uint, AdventureFadaGump> _fadas = new Dictionary<uint, AdventureFadaGump>();
        private readonly uint _dono;

        private readonly ResizableStaticPic _pic;
        private int _nivel, _tam = 20;
        private uint _arte;
        private ushort _cor;
        private int _estado;
        private uint _inicioEstado, _fimEstado;
        private uint _alvo;

        public AdventureFadaGump(World world, uint dono) : base(world, 0, 0)
        {
            _dono = dono;
            CanMove = false;
            AcceptMouseInput = false;
            CanCloseWithRightClick = false;
            WantUpdateSize = false;
            Width = 32;
            Height = 32;
            _pic = new ResizableStaticPic(0, 20, 20);
            Add(_pic);
        }

        // Fada do proprio player (pacote AD03)
        public static void Receber(World world, int nivel, int estado, ushort arte, ushort cor, uint alvo)
        {
            if (world.Player != null)
            {
                Receber(world, world.Player.Serial, nivel, estado, arte, cor, alvo);
            }
        }

        // Fada de qualquer player (pacote AD05): uma por dono
        public static void Receber(World world, uint dono, int nivel, int estado, ushort arte, ushort cor, uint alvo)
        {
            if (!_fadas.TryGetValue(dono, out AdventureFadaGump fada) || fada.IsDisposed)
            {
                if (nivel <= 0)
                {
                    return;
                }

                fada = new AdventureFadaGump(world, dono);
                _fadas[dono] = fada;
                UIManager.Add(fada);
            }

            fada.Definir(nivel, estado, arte, cor, alvo);
        }

        public override void Dispose()
        {
            if (_fadas.TryGetValue(_dono, out AdventureFadaGump f) && f == this)
            {
                _fadas.Remove(_dono);
            }

            base.Dispose();
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
                _fimEstado = Time.Ticks + (estado == 2 ? TEMPO_ATAQUE : estado == 3 ? TEMPO_EXPLORA : TEMPO_CURA);
            }

            // Tamanho cresce com o nivel: 20 no nivel 1 ate 32 no nivel 5 (a arte tem 32)
            _tam = 17 + Math.Min(Math.Max(nivel, 1), 5) * 3;
        }

        // Ponto na tela (ja com camera) em cima de um mobile ou item
        private static Point Tela(GameObject m, int dx, int dy)
        {
            int wx = (int)(m.RealScreenPosition.X + m.Offset.X + 22) + dx;
            int wy = (int)(m.RealScreenPosition.Y + (m.Offset.Y - m.Offset.Z)) + dy;
            Point p = Client.Game.Scene.Camera.WorldToScreen(new Point(wx, wy));
            Camera camera = Client.Game.Scene.Camera;

            return new Point(p.X + camera.Bounds.X, p.Y + camera.Bounds.Y);
        }

        public override bool Draw(UltimaBatcher2D batcher, int x, int y)
        {
            Mobile dono = World.Mobiles.Get(_dono);   // o dono desta fada (pode ser outro player)

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

            // Exploracao: voa ate o item achado, fica brilhando em cima dele e volta
            if (_estado == 3 && _alvo != 0)
            {
                Entity item = World.Get(_alvo);

                if (item != null)
                {
                    Point alvo = Tela(item, 0, -18);
                    float f = (Time.Ticks - _inicioEstado) / (float)TEMPO_EXPLORA;
                    float ida = f < 0.25f ? f / 0.25f : (f > 0.8f ? (1f - f) / 0.2f : 1f);   // vai, fica, volta
                    ida = ida * ida * (3 - 2 * ida);
                    pos = new Point(casa.X + (int)((alvo.X - casa.X) * ida), casa.Y + (int)((alvo.Y - casa.Y) * ida) + (int)(Math.Sin(t * 6) * 3));
                    voando = true;

                    // brilho girando em volta do item enquanto ela esta la
                    if (ida > 0.95f)
                    {
                        for (int i = 0; i < 10; i++)
                        {
                            double a = i / 10.0 * Math.PI * 2 + t * 4;
                            int bx = alvo.X + (int)(Math.Cos(a) * 16);
                            int by = alvo.Y + 14 + (int)(Math.Sin(a) * 6);
                            batcher.Draw(SolidColorTextureCache.GetTexture(new Color(255, 240, 160)), new Rectangle(bx - 2, by - 2, 4, 4), ShaderHueTranslator.GetHueVector(0, false, 0.85f));
                        }
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
