// SPDX-License-Identifier: BSD-2-Clause
// ==========================================================================
// ADVENTURE: FAIXA DE VIDA / MANA / STAMINA NO TOPO DO JOGO (opcao C da previa).
//
// - Faixa escura com linha dourada embaixo: nome do personagem e 3 barras: VIDA (vermelha),
//   MANA (azul), STAM (verde), com o valor "atual/maximo" escrito dentro. Estilo da barra de boss.
// - Nao depende de pacote do servidor: usa a vida/mana/stam que o cliente ja recebe do Sphere.
// - Arrastar com o mouse: muda de lugar. Rodinha do mouse em cima: maior / menor (igual ao .painel).
//   Posicao e tamanho ficam salvos em adventure_status.txt (pasta do perfil).
// - Liga/desliga em Opcoes > Interface > Avisos Adventure > "Faixa de vida/mana/stam no topo".
// ==========================================================================

using System;
using System.IO;
using ClassicUO.Assets;
using ClassicUO.Configuration;
using ClassicUO.Game.GameObjects;
using ClassicUO.Game.Managers;
using ClassicUO.Game.UI.Controls;
using ClassicUO.Input;
using ClassicUO.Renderer;
using Microsoft.Xna.Framework;

namespace ClassicUO.Game.UI.Gumps
{
    public class AdventureStatusGump : Gump
    {
        // ---------- ajustes ----------
        // tamanhos no zoom 1.0 (a rodinha do mouse multiplica tudo)
        private const int ALT_BASE = 20;        // altura da faixa
        private const int ALT_BARRA_BASE = 12;  // altura de cada barra
        private const int LARG_NOME_BASE = 90;  // espaco do nome a esquerda
        private const int LARG_BARRA_BASE = 150;// largura de cada barra
        private const int ESPACO_BASE = 8;      // espaco entre as barras
        private const float ZOOM_MIN = 0.7f, ZOOM_MAX = 2.0f, ZOOM_PADRAO = 1.0f, ZOOM_PASSO = 0.1f;
        private const string ARQUIVO = "adventure_status.txt";

        private float _zoom = ZOOM_PADRAO;
        private int ALT, ALT_BARRA, LARG_NOME, LARG_BARRA, ESPACO;

        private static readonly Color COR_FUNDO = new Color(10, 8, 6);
        private static readonly Color COR_OURO = new Color(200, 160, 70);
        private static readonly Color COR_OURO2 = new Color(110, 85, 40);
        private static readonly Color COR_TRILHO = new Color(22, 19, 16);

        // cor escura (esquerda) e clara (brilho de cima) de cada barra
        private static readonly string[] NOMES = { "VIDA", "MANA", "STAM" };
        private static readonly Color[] COR_BARRA = { new Color(185, 30, 28), new Color(40, 90, 205), new Color(45, 150, 55) };
        private static readonly Color[] COR_BRILHO = { new Color(240, 85, 70), new Color(95, 155, 245), new Color(120, 215, 100) };

        private static AdventureStatusGump _instancia;

        private readonly TextBox[] _rotulo = new TextBox[3];
        private readonly TextBox[] _valor = new TextBox[3];
        private readonly string[] _textoValor = { "", "", "" };
        private TextBox _nome;
        private string _textoNome = "";

        public AdventureStatusGump(World world) : base(world, 0, 0)
        {
            _instancia = this;
            CanMove = true;
            AcceptMouseInput = true;
            CanCloseWithRightClick = false;   // sempre na tela (desliga pelas opcoes)
            WantUpdateSize = false;
            CarregarPosicao();   // posicao e zoom salvos
            Montar();
        }

        // calcula os tamanhos pelo zoom e recria os textos
        private void Montar()
        {
            ALT = (int)(ALT_BASE * _zoom);
            ALT_BARRA = (int)(ALT_BARRA_BASE * _zoom);
            LARG_NOME = (int)(LARG_NOME_BASE * _zoom);
            LARG_BARRA = (int)(LARG_BARRA_BASE * _zoom);
            ESPACO = Math.Max(4, (int)(ESPACO_BASE * _zoom));
            Width = LARG_NOME + LARG_BARRA * 3 + ESPACO * 2 + 8;
            Height = ALT;
            int fonte = Math.Max(8, (int)Math.Round(9 * _zoom));

            for (int i = 0; i < 3; i++)
            {
                _rotulo[i]?.Dispose();
                _rotulo[i] = TextBox.GetOne(NOMES[i], EmbeddedFontNames.ROBOTO_BOLD, fonte, Color.White, TextBox.RTLOptions.Default(60));
                _valor[i]?.Dispose();
                _valor[i] = null;   // recria no proximo Update, no tamanho novo
            }

            _nome?.Dispose();
            _nome = null;
        }

        // rodinha do mouse em cima da faixa: maior / menor, e salva
        public override void OnMouseWheel(MouseEventType delta)
        {
            float antes = _zoom;

            if (delta == MouseEventType.WheelScrollUp)
            {
                _zoom = Math.Min(ZOOM_MAX, _zoom + ZOOM_PASSO);
            }
            else if (delta == MouseEventType.WheelScrollDown)
            {
                _zoom = Math.Max(ZOOM_MIN, _zoom - ZOOM_PASSO);
            }

            if (Math.Abs(antes - _zoom) > 0.001f)
            {
                Montar();
                SalvarPosicao();
            }
        }

        // ---------- chamados de fora ----------

        // Abre no login, se a opcao estiver ligada
        public static void AbrirSeLigada(World world)
        {
            if (ProfileManager.CurrentProfile?.AdvBarraStatus == false)
            {
                Fechar();

                return;
            }

            if (_instancia == null || _instancia.IsDisposed)
            {
                UIManager.Add(new AdventureStatusGump(world));
            }
        }

        public static void Fechar()
        {
            if (_instancia != null && !_instancia.IsDisposed)
            {
                _instancia.Dispose();
            }

            _instancia = null;
        }

        // ---------- posicao e zoom (arquivo: "x|y|zoom") ----------
        private string CaminhoArquivo()
        {
            return Path.Combine(ProfileManager.ProfilePath ?? "", ARQUIVO);
        }

        private void CarregarPosicao()
        {
            // padrao: no topo, centralizada
            X = Math.Max(0, (ScaleHelper.LogicalWindowWidth - (int)((LARG_NOME_BASE + LARG_BARRA_BASE * 3 + ESPACO_BASE * 2 + 8) * ZOOM_PADRAO)) / 2);
            Y = 0;

            try
            {
                string arq = CaminhoArquivo();

                if (File.Exists(arq))
                {
                    string[] p = File.ReadAllText(arq).Trim().Split('|');

                    if (p.Length >= 3 && int.TryParse(p[0], out int x) && int.TryParse(p[1], out int y)
                        && float.TryParse(p[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float z))
                    {
                        X = Math.Max(0, x);
                        Y = Math.Max(0, y);
                        _zoom = Math.Max(ZOOM_MIN, Math.Min(ZOOM_MAX, z));
                    }
                }
            }
            catch
            {
                // sem arquivo: fica no topo
            }
        }

        private void SalvarPosicao()
        {
            try
            {
                File.WriteAllText(CaminhoArquivo(), X + "|" + Y + "|" + _zoom.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            catch
            {
                // sem problema: volta para o topo no proximo login
            }
        }

        protected override void OnDragEnd(int x, int y)
        {
            base.OnDragEnd(x, y);
            X = Math.Max(0, Math.Min(X, ScaleHelper.LogicalWindowWidth - 40));
            Y = Math.Max(0, Math.Min(Y, ScaleHelper.LogicalWindowHeight - ALT));
            SalvarPosicao();
        }

        // ---------- atualizacao ----------
        public override void Update()
        {
            base.Update();

            if (IsDisposed)
            {
                return;
            }

            PlayerMobile p = World.Player;

            if (p == null)
            {
                return;
            }

            AtualizarTexto(0, p.Hits, p.HitsMax);
            AtualizarTexto(1, p.Mana, p.ManaMax);
            AtualizarTexto(2, p.Stamina, p.StaminaMax);

            string nome = string.IsNullOrEmpty(p.Name) ? "" : p.Name;

            if (_nome == null || nome != _textoNome)
            {
                _textoNome = nome;
                _nome?.Dispose();
                _nome = TextBox.GetOne(nome, EmbeddedFontNames.ROBOTO_BOLD, Math.Max(8, (int)Math.Round(10 * _zoom)), new Color(235, 195, 110), TextBox.RTLOptions.Default(LARG_NOME - 8));
            }
        }

        // so recria o texto quando o valor muda (economiza)
        private void AtualizarTexto(int i, int atual, int maximo)
        {
            string t = atual + "/" + maximo;

            if (_valor[i] != null && t == _textoValor[i])
            {
                return;
            }

            _textoValor[i] = t;
            _valor[i]?.Dispose();
            _valor[i] = TextBox.GetOne(t, EmbeddedFontNames.ROBOTO_BOLD, Math.Max(8, (int)Math.Round(9 * _zoom)), Color.White, TextBox.RTLOptions.DefaultRightAligned(LARG_BARRA / 2));
        }

        // ---------- desenho ----------
        public override bool Draw(UltimaBatcher2D batcher, int x, int y)
        {
            if (!base.Draw(batcher, x, y))
            {
                return false;
            }

            PlayerMobile p = World.Player;

            if (p == null)
            {
                return true;
            }

            // faixa escura + linha dourada embaixo
            Ret(batcher, x, y, Width, ALT, COR_FUNDO, 0.85f);
            Ret(batcher, x, y + ALT - 1, Width, 1, COR_OURO, 0.9f);

            Ret(batcher, x, y, 1, ALT, COR_OURO2, 0.9f);
            Ret(batcher, x + Width - 1, y, 1, ALT, COR_OURO2, 0.9f);
            _nome?.Draw(batcher, x + 6, y + (ALT - (_nome?.Height ?? 0)) / 2);

            int larg = LARG_BARRA;
            int bx = x + LARG_NOME;
            int by = y + (ALT - ALT_BARRA) / 2;

            DesenhaBarra(batcher, 0, bx, by, larg, p.Hits, p.HitsMax);
            DesenhaBarra(batcher, 1, bx + (larg + ESPACO), by, larg, p.Mana, p.ManaMax);
            DesenhaBarra(batcher, 2, bx + (larg + ESPACO) * 2, by, larg, p.Stamina, p.StaminaMax);

            return true;
        }

        private void DesenhaBarra(UltimaBatcher2D batcher, int i, int x, int y, int w, int atual, int maximo)
        {
            float pct = maximo > 0 ? Math.Max(0f, Math.Min(1f, atual / (float)maximo)) : 0f;
            int cheio = (int)((w - 2) * pct);

            Ret(batcher, x, y, w, ALT_BARRA, COR_TRILHO, 0.95f);                       // trilho escuro
            Ret(batcher, x + 1, y + 1, cheio, ALT_BARRA - 2, COR_BARRA[i]);             // valor
            Ret(batcher, x + 1, y + 1, cheio, (ALT_BARRA - 2) / 3, COR_BRILHO[i], 0.6f); // brilho em cima
            Ret(batcher, x + 1 + cheio - 2, y + 1, Math.Min(2, cheio), ALT_BARRA - 2, COR_BRILHO[i], 0.8f); // ponta clara (degrade)
            Borda(batcher, x, y, w, ALT_BARRA, COR_OURO2);

            int ty = y + (ALT_BARRA - (_rotulo[i]?.Height ?? 14)) / 2;
            _rotulo[i]?.Draw(batcher, x + 4, ty);
            _valor[i]?.Draw(batcher, x + w - LARG_BARRA / 2 - 4, ty);
        }

        private static void Ret(UltimaBatcher2D batcher, int x, int y, int w, int h, Color cor, float alpha = 1f)
        {
            if (w <= 0 || h <= 0)
            {
                return;
            }

            batcher.Draw(SolidColorTextureCache.GetTexture(cor), new Rectangle(x, y, w, h), ShaderHueTranslator.GetHueVector(0, false, alpha));
        }

        private static void Borda(UltimaBatcher2D batcher, int x, int y, int w, int h, Color cor)
        {
            batcher.DrawRectangle(SolidColorTextureCache.GetTexture(cor), x, y, w, h, ShaderHueTranslator.GetHueVector(0, false, 1f));
        }

        public override void Dispose()
        {
            for (int i = 0; i < 3; i++)
            {
                _rotulo[i]?.Dispose();
                _valor[i]?.Dispose();
            }

            _nome?.Dispose();

            if (_instancia == this)
            {
                _instancia = null;
            }

            base.Dispose();
        }
    }
}
