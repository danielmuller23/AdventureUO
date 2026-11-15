// SPDX-License-Identifier: BSD-2-Clause
// ==========================================================================
// ADVENTURE: FADA do player. Desenhada pelo proprio cliente, voando ao lado do
// personagem (flutua para cima e para baixo). Nao existe NPC nem item no mundo.
//
// Pacote proprio 0xBF sub 0xAD03 (11 bytes):
//    [nivel byte][estado byte][arte word][cor word]
//    nivel 0 = sem fada. estado: 0 normal, 1 curando (pisca verde), 2 atacando (arte+1)
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
        private const uint TEMPO_ESTADO = 1200;   // ms que a cura/ataque fica na tela

        private static AdventureFadaGump _instancia;

        private readonly ResizableStaticPic _pic;
        private int _nivel;
        private uint _arte;
        private ushort _cor;
        private int _estado;
        private uint _fimEstado;

        public AdventureFadaGump(World world) : base(world, 0, 0)
        {
            _instancia = this;
            CanMove = false;
            AcceptMouseInput = false;
            CanCloseWithRightClick = false;
            WantUpdateSize = false;
            Width = 40;
            Height = 40;
            _pic = new ResizableStaticPic(0, 30, 30);
            Add(_pic);
        }

        public static void Receber(World world, int nivel, int estado, ushort arte, ushort cor)
        {
            if (_instancia == null || _instancia.IsDisposed)
            {
                if (nivel <= 0)
                {
                    return;
                }

                UIManager.Add(new AdventureFadaGump(world));
            }

            _instancia.Definir(nivel, estado, arte, cor);
        }

        private void Definir(int nivel, int estado, ushort arte, ushort cor)
        {
            _nivel = nivel;
            _arte = arte;
            _cor = cor;

            if (estado > 0)
            {
                _estado = estado;
                _fimEstado = Time.Ticks + TEMPO_ESTADO;
            }

            // Tamanho cresce com o nivel: 24 no nivel 1 ate 36 no nivel 5
            int tam = 21 + Math.Min(Math.Max(nivel, 1), 5) * 3;
            _pic.Width = tam;
            _pic.Height = tam;
            Width = tam;
            Height = tam;
        }

        public override bool Draw(UltimaBatcher2D batcher, int x, int y)
        {
            Mobile m = World.Player;

            if (_nivel <= 0 || m == null || _arte == 0)
            {
                return false;
            }

            if (_estado > 0 && Time.Ticks >= _fimEstado)
            {
                _estado = 0;
            }

            // Arte e cor do momento
            _pic.Graphic = _estado == 2 ? _arte + 1 : _arte;
            _pic.Hue = _estado == 1 ? HUE_CURA : _cor;

            // Flutua: sobe e desce devagar, e da uma voltinha para os lados
            double t = Time.Ticks / 1000.0;
            int bobY = (int)(Math.Sin(t * 3.0) * 4);
            int bobX = (int)(Math.Cos(t * 1.5) * 3);

            // Ao lado do ombro do personagem (mais alto se estiver montado)
            int wx = (int)(m.RealScreenPosition.X + m.Offset.X + 22) + 20 + bobX;
            int wy = (int)(m.RealScreenPosition.Y + (m.Offset.Y - m.Offset.Z)) - (m.IsMounted ? 75 : 55) + bobY;

            Point p = Client.Game.Scene.Camera.WorldToScreen(new Point(wx, wy));
            Camera camera = Client.Game.Scene.Camera;
            X = p.X + camera.Bounds.X - (Width >> 1);
            Y = p.Y + camera.Bounds.Y - (Height >> 1);

            return base.Draw(batcher, X, Y);
        }
    }
}
