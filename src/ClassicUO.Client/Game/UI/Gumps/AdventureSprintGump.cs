// SPDX-License-Identifier: BSD-2-Clause
// ==========================================================================
// ADVENTURE: barra fixa da ARRANCADA (sempre na tela).
//  - Inativa: raio cinza, barra escura.
//  - Ativa:   raio aceso, barra verde esvaziando com os segundos que faltam.
//  O servidor liga com o buff 1005 (pacote 0xDF) e desliga quando remove o buff.
//  A posicao fica salva no arquivo adventure_arrancada.txt da pasta do perfil.
// ==========================================================================

using System;
using System.IO;
using ClassicUO.Configuration;
using ClassicUO.Game.Managers;
using ClassicUO.Game.UI.Controls;
using ClassicUO.Renderer;
using Microsoft.Xna.Framework;

namespace ClassicUO.Game.UI.Gumps
{
    public class AdventureSprintGump : Gump
    {
        public const ushort BUFF_TYPE = 1005;   // numero do buff da arrancada (CR_ICONE no Sphere)
        private const uint RAIO_ART = 0x3A5C;   // raio do trofeu (art.mul, aba Items)
        private const int LARGURA = 150, ALTURA = 26;
        private const ushort HUE_RAIO_ATIVO = 0x0035;    // amarelo
        private const ushort HUE_RAIO_INATIVO = 0x0386;  // cinza
        private const ushort HUE_TEXTO_ATIVO = 0x0481;   // branco
        private const ushort HUE_TEXTO_INATIVO = 0x03B2; // cinza escuro

        private static AdventureSprintGump _instancia;

        private readonly ResizableStaticPic _raio;
        private readonly AlphaBlendControl _verde;
        private readonly Label _texto;
        private uint _inicio, _fim;  // em milissegundos (Time.Ticks)
        private bool _ativa;
        private int _ultimoSegundo = -1;

        public AdventureSprintGump(World world) : base(world, 0, 0)
        {
            _instancia = this;
            Width = LARGURA;
            Height = ALTURA;
            CanMove = true;
            CanCloseWithRightClick = false;   // sempre na tela
            AcceptMouseInput = true;
            WantUpdateSize = false;

            // Fundo escuro semitransparente (sem a borda antiga)
            Add(new AlphaBlendControl(0.75f) { Width = LARGURA, Height = ALTURA, BaseColor = new Color(15, 15, 15) });
            // Quadrado do icone
            Add(new AlphaBlendControl(0.9f) { X = 2, Y = 2, Width = 22, Height = ALTURA - 4, BaseColor = new Color(30, 30, 30) });
            _raio = new ResizableStaticPic(RAIO_ART, 22, ALTURA - 4) { X = 2, Y = 2, Hue = HUE_RAIO_INATIVO };
            Add(_raio);
            // Trilho escuro da barra
            Add(new AlphaBlendControl(0.9f) { X = 28, Y = 5, Width = LARGURA - 32, Height = ALTURA - 10, BaseColor = new Color(35, 35, 35) });
            // Barra verde (comeca vazia)
            _verde = new AlphaBlendControl(0.92f) { X = 28, Y = 5, Width = 0, Height = ALTURA - 10, BaseColor = new Color(60, 190, 70) };
            Add(_verde);
            _texto = new Label("ARRANCADA", true, HUE_TEXTO_INATIVO, 0, 1) { X = 34, Y = 5 };
            Add(_texto);

            CarregarPosicao();
        }

        // Chamado quando o servidor manda o buff 1005
        public static void Ativar(World world, uint segundos)
        {
            AbrirSeFechada(world);
            _instancia?.Iniciar(segundos);
        }

        // Chamado quando o servidor tira o buff 1005
        public static void Desativar()
        {
            _instancia?.Parar();
        }

        // Abre a barra no login (e se por algum motivo tiver sido fechada)
        public static void AbrirSeFechada(World world)
        {
            if (_instancia == null || _instancia.IsDisposed)
            {
                UIManager.Add(new AdventureSprintGump(world));
            }
        }

        private void Iniciar(uint segundos)
        {
            if (segundos == 0)
            {
                segundos = 1;
            }

            _inicio = Time.Ticks;
            _fim = Time.Ticks + segundos * 1000;
            _ativa = true;
            _ultimoSegundo = -1;
            _raio.Hue = HUE_RAIO_ATIVO;
            _texto.Hue = HUE_TEXTO_ATIVO;
        }

        private void Parar()
        {
            _ativa = false;
            _verde.Width = 0;
            _raio.Hue = HUE_RAIO_INATIVO;
            _texto.Hue = HUE_TEXTO_INATIVO;
            _texto.Text = "ARRANCADA";
        }

        public override void Update()
        {
            base.Update();

            if (!_ativa)
            {
                return;
            }

            if (Time.Ticks >= _fim)
            {
                Parar();   // o tempo acabou mesmo que o servidor nao tenha avisado
                return;
            }

            float resto = (_fim - Time.Ticks) / (float)(_fim - _inicio);
            _verde.Width = (int)((LARGURA - 32) * resto);

            int segundos = (int)Math.Ceiling((_fim - Time.Ticks) / 1000f);

            if (segundos != _ultimoSegundo)
            {
                _ultimoSegundo = segundos;
                _texto.Text = "ARRANCADA  " + segundos + "s";
            }
        }

        public override bool Draw(UltimaBatcher2D batcher, int x, int y)
        {
            if (!base.Draw(batcher, x, y))
            {
                return false;
            }

            // Contorno fino cinza escuro
            batcher.DrawRectangle(SolidColorTextureCache.GetTexture(new Color(60, 60, 60)), x, y, LARGURA, ALTURA, ShaderHueTranslator.GetHueVector(0, false, 1f));

            return true;
        }

        // ---- posicao salva no perfil ----
        private static string ArquivoPosicao()
        {
            return string.IsNullOrEmpty(ProfileManager.ProfilePath) ? null : Path.Combine(ProfileManager.ProfilePath, "adventure_arrancada.txt");
        }

        private void CarregarPosicao()
        {
            X = 100;
            Y = 100;

            try
            {
                string arq = ArquivoPosicao();

                if (arq != null && File.Exists(arq))
                {
                    string[] p = File.ReadAllText(arq).Split(' ');

                    if (p.Length >= 2 && int.TryParse(p[0], out int px) && int.TryParse(p[1], out int py))
                    {
                        X = px;
                        Y = py;
                    }
                }
            }
            catch
            {
                // sem arquivo de posicao: usa a posicao padrao
            }
        }

        public override void Dispose()
        {
            try
            {
                string arq = ArquivoPosicao();

                if (arq != null)
                {
                    File.WriteAllText(arq, X + " " + Y);
                }
            }
            catch
            {
                // nao conseguiu salvar a posicao: sem problema
            }

            if (_instancia == this)
            {
                _instancia = null;
            }

            base.Dispose();
        }
    }
}
