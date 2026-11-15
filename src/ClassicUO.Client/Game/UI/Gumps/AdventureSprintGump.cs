// SPDX-License-Identifier: BSD-2-Clause
// ==========================================================================
// ADVENTURE: PAINEL DE TIMERS fixo (sempre na tela). Substitui o .painel antigo (dialog).
//  Linhas: 0 Arrancada, 1 Magia, 2 Skill, 3 Pocao, 4 Bandagem.
//  - Pronto:     icone cinza, barra escura, texto apagado.
//  - Esperando:  icone aceso, barra colorida esvaziando, segundos com decimo.
//  O servidor manda o pacote proprio 0xBF sub 0xAD02:
//     [linha byte][tempo em milissegundos dword][id word]
//     id = numero da magia (linha 1) ou da skill (linha 2) para mostrar o nome.
//     tempo 0 = linha pronta na hora (magia saiu, skill acabou, etc).
//  A arrancada tambem liga pelo buff 1005 (pacote 0xDF), como antes.
//  A posicao fica salva no arquivo adventure_painel.txt da pasta do perfil.
// ==========================================================================

using System;
using System.IO;
using ClassicUO.Configuration;
using ClassicUO.Game.Data;
using ClassicUO.Game.Managers;
using ClassicUO.Game.UI.Controls;
using ClassicUO.Renderer;
using Microsoft.Xna.Framework;

namespace ClassicUO.Game.UI.Gumps
{
    public class AdventurePainelGump : Gump
    {
        public const ushort BUFF_ARRANCADA = 1005; // numero do buff da arrancada (CR_ICONE no Sphere)

        private const int LARGURA = 190, ALTURA_LINHA = 22, ESPACO = 3, MARGEM = 4;
        private const ushort HUE_PRONTO = 0x0386;        // cinza (icone apagado)
        private const ushort HUE_TEXTO_ATIVO = 0x0481;   // branco
        private const ushort HUE_TEXTO_PRONTO = 0x03B2;  // cinza escuro

        // Configuracao de cada linha: nome, arte do icone (art.mul), cor do icone aceso, cor da barra
        private static readonly string[] NOMES = { "ARRANCADA", "MAGIA", "SKILL", "POCAO", "BANDAGEM" };
        private static readonly uint[] ICONES = { 0x3A5C, 0x0EFA, 0x13B9, 0x0F0C, 0x0E21 };    // raio, livro, espada, pocao, bandagem
        private static readonly ushort[] HUE_ICONE = { 0x0035, 0, 0, 0, 0 };                  // raio pintado de amarelo; os outros com a cor original
        private static readonly Color[] CORES =
        {
            new Color(60, 190, 70),   // arrancada: verde
            new Color(70, 120, 230),  // magia: azul
            new Color(230, 130, 40),  // skill: laranja
            new Color(60, 170, 90),   // pocao: verde
            new Color(220, 200, 40)   // bandagem: amarelo
        };

        private static AdventurePainelGump _instancia;

        private readonly Linha[] _linhas = new Linha[NOMES.Length];

        private sealed class Linha
        {
            public ResizableStaticPic Icone;
            public AlphaBlendControl Barra;
            public Label Texto;
            public uint Inicio, Fim;
            public bool Ativa;
            public string Extra = "";
            public int UltimoDecimo = -1;
        }

        public AdventurePainelGump(World world) : base(world, 0, 0)
        {
            _instancia = this;
            int altura = MARGEM * 2 + NOMES.Length * (ALTURA_LINHA + ESPACO) - ESPACO;
            Width = LARGURA;
            Height = altura;
            CanMove = true;
            CanCloseWithRightClick = false;   // sempre na tela
            AcceptMouseInput = true;
            WantUpdateSize = false;

            // Fundo escuro semitransparente (sem borda antiga)
            Add(new AlphaBlendControl(0.7f) { Width = LARGURA, Height = altura, BaseColor = new Color(10, 10, 10) });

            for (int i = 0; i < NOMES.Length; i++)
            {
                int y = MARGEM + i * (ALTURA_LINHA + ESPACO);
                var l = new Linha();

                Add(new AlphaBlendControl(0.9f) { X = MARGEM, Y = y, Width = 20, Height = ALTURA_LINHA, BaseColor = new Color(30, 30, 30) });
                l.Icone = new ResizableStaticPic(ICONES[i], 20, ALTURA_LINHA) { X = MARGEM, Y = y, Hue = HUE_PRONTO };
                Add(l.Icone);

                int bx = MARGEM + 24;
                Add(new AlphaBlendControl(0.9f) { X = bx, Y = y + 2, Width = LARGURA - bx - MARGEM, Height = ALTURA_LINHA - 4, BaseColor = new Color(35, 35, 35) });
                l.Barra = new AlphaBlendControl(0.92f) { X = bx, Y = y + 2, Width = 0, Height = ALTURA_LINHA - 4, BaseColor = CORES[i] };
                Add(l.Barra);

                l.Texto = new Label(NOMES[i], true, HUE_TEXTO_PRONTO, 0, 1) { X = bx + 5, Y = y + 3 };
                Add(l.Texto);

                _linhas[i] = l;
            }

            CarregarPosicao();
        }

        // ---------- chamados de fora ----------

        // Pacote 0xBF 0xAD02: liga (ms > 0) ou deixa pronta (ms = 0) uma linha
        public static void Receber(World world, int linha, uint ms, ushort id)
        {
            AbrirSeFechada(world);
            _instancia?.Definir(linha, ms, id);
        }

        // Buff 1005 (arrancada) pelo pacote 0xDF
        public static void AtivarArrancada(World world, uint segundos)
        {
            Receber(world, 0, (segundos == 0 ? 1 : segundos) * 1000, 0);
        }

        public static void DesativarArrancada()
        {
            _instancia?.Definir(0, 0, 0);
        }

        // Abre o painel no login (e se por algum motivo tiver sido fechado)
        public static void AbrirSeFechada(World world)
        {
            if (_instancia == null || _instancia.IsDisposed)
            {
                UIManager.Add(new AdventurePainelGump(world));
            }
        }

        // ---------- logica ----------

        private void Definir(int linha, uint ms, ushort id)
        {
            if (linha < 0 || linha >= _linhas.Length)
            {
                return;
            }

            Linha l = _linhas[linha];

            if (ms == 0)
            {
                Pronta(linha);

                return;
            }

            l.Extra = NomeExtra(linha, id);
            l.Inicio = Time.Ticks;
            l.Fim = Time.Ticks + ms;
            l.Ativa = true;
            l.UltimoDecimo = -1;
            l.Icone.Hue = HUE_ICONE[linha];
            l.Texto.Hue = HUE_TEXTO_ATIVO;
        }

        private string NomeExtra(int linha, ushort id)
        {
            try
            {
                if (linha == 1 && id > 0)
                {
                    string n = SpellsMagery.GetSpell(id).Name;

                    return string.IsNullOrEmpty(n) ? "" : "  " + n;
                }

                if (linha == 2 && id < Client.Game.UO.FileManager.Skills.Skills.Count)
                {
                    string n = Client.Game.UO.FileManager.Skills.Skills[id].Name;

                    return string.IsNullOrEmpty(n) ? "" : "  " + n;
                }
            }
            catch
            {
                // sem nome: mostra so o tempo
            }

            return "";
        }

        private void Pronta(int linha)
        {
            Linha l = _linhas[linha];
            l.Ativa = false;
            l.Barra.Width = 0;
            l.Icone.Hue = HUE_PRONTO;
            l.Texto.Hue = HUE_TEXTO_PRONTO;
            l.Texto.Text = NOMES[linha];
        }

        public override void Update()
        {
            base.Update();

            int larguraBarra = LARGURA - (MARGEM + 24) - MARGEM;

            for (int i = 0; i < _linhas.Length; i++)
            {
                Linha l = _linhas[i];

                if (!l.Ativa)
                {
                    continue;
                }

                if (Time.Ticks >= l.Fim)
                {
                    Pronta(i);   // o tempo acabou: pronta
                    continue;
                }

                float resto = (l.Fim - Time.Ticks) / (float)(l.Fim - l.Inicio);
                l.Barra.Width = (int)(larguraBarra * resto);

                int decimos = (int)Math.Ceiling((l.Fim - Time.Ticks) / 100f);

                if (decimos != l.UltimoDecimo)
                {
                    l.UltimoDecimo = decimos;
                    l.Texto.Text = NOMES[i] + l.Extra + "  " + (decimos / 10) + "," + (decimos % 10) + "s";
                }
            }
        }

        public override bool Draw(UltimaBatcher2D batcher, int x, int y)
        {
            if (!base.Draw(batcher, x, y))
            {
                return false;
            }

            // Contorno fino cinza escuro
            batcher.DrawRectangle(SolidColorTextureCache.GetTexture(new Color(60, 60, 60)), x, y, Width, Height, ShaderHueTranslator.GetHueVector(0, false, 1f));

            return true;
        }

        // ---------- posicao salva no perfil ----------
        private static string ArquivoPosicao()
        {
            return string.IsNullOrEmpty(ProfileManager.ProfilePath) ? null : Path.Combine(ProfileManager.ProfilePath, "adventure_painel.txt");
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
