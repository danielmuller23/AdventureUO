// SPDX-License-Identifier: BSD-2-Clause

using ClassicUO.Assets;
using ClassicUO.Configuration;
using ClassicUO.Game.Data;
using ClassicUO.Game.Scenes;
using ClassicUO.Game.UI.Controls;
using ClassicUO.Renderer;
using ClassicUO.Utility;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;

namespace ClassicUO.Game.GameObjects
{
    public partial class Mobile
    {
        // ADVENTURE: efeitos no corpo ate este Time.Ticks (pacote 0xBF 0xAD04). 0 = sem efeito
        public uint AdvFogoAte;       // tipo 2 Flamestrike
        public uint AdvMaldicaoAte;   // tipo 6 Curse
        public uint AdvGeloAte;       // tipo 7 Paralyze
        public uint AdvEscudoAte;     // tipo 4 Protection
        public uint AdvEscudoInicio;  // quando o Protection comecou (para a explosao de luz inicial)
        // cores (hues do UO). Fogo de dentro pra fora: vermelho forte -> laranja -> amarelo nas pontas
        private static readonly ushort[] AdvHuesFogo = { 0x0021, 0x0026, 0x002B, 0x002E, 0x0030, 0x0030, 0x0035, 0x0035, 0x0036, 0x0036, 0x0035 };   // mais dourado nas pontas
        private static readonly ushort[] AdvHuesEscuro = { 0x0001 };
        private static readonly ushort[] AdvHuesFumaca = { 0x0013, 0x0014, 0x0015, 0x0014, 0x0015 };
        private static readonly ushort[] AdvHuesGeloPinta = { 0x0482 };            // pinta o corpo de gelo (branco-azulado)
        private static readonly ushort[] AdvHuesGelo = { 0x0481, 0x0482 };       // brilho branco piscando por cima
        private static readonly ushort[] AdvHuesEscudo = { 0x0059, 0x0058, 0x0059, 0x005A, 0x0058, 0x0059 };   // aura AZUL do Protection (padrao das magias de protecao)

        private const int SIT_OFFSET_Y = 4;
        private EquipConvData? _equipConvData;
        private int _characterFrameStartY;
        private int _startCharacterWaistY;
        private int _startCharacterKneesY;
        private TextBox _nameText;
        // Keeps the overhead stack still while the sprite top moves with every animation frame.
        private OverheadStackAnchor _overheadStackAnchor;
        // Top of the overhead stack relative to the walk-interpolated anchor, so it stays valid as the camera moves.
        private int _overheadStackTopOffset;
        private uint _overheadStackTick;
        private bool _hasOverheadStack;
        private int _namePlateHeight;
        private uint _namePlateTick;

        // A value not refreshed for this long belongs to a mobile no longer drawn.
        private const uint OVERHEAD_STACK_STALE_MS = 250;
        // Space between stacked overheads.
        private const int OVERHEAD_GAP = 2;

        /// <summary>
        /// Top of what this mobile draws above itself (sprite, simple healthbar, name), in world coordinates.
        /// Overheads drawn later in the frame (HP percentage, nameplate) stack above it.
        /// </summary>
        /// <param name="topY">World Y of the stack top.</param>
        /// <returns>False when the mobile was not drawn recently; fall back to its animation dimensions.</returns>
        internal bool TryGetOverheadStackTop(out int topY)
        {
            topY = RealScreenPosition.Y + (int)(Offset.Y - Offset.Z) + _overheadStackTopOffset;

            return _hasOverheadStack && Time.Ticks - _overheadStackTick <= OVERHEAD_STACK_STALE_MS;
        }

        /// <summary>
        /// Records that this mobile's nameplate was drawn, so the HP percentage can sit above it.
        /// </summary>
        /// <param name="height">Nameplate height in screen pixels, or 0 when the plate is hidden this draw.</param>
        internal void SetNamePlateDrawn(int height)
        {
            _namePlateHeight = height;
            _namePlateTick = Time.Ticks;
        }

        /// <summary>
        /// Height of this mobile's nameplate in screen pixels, as its last draw recorded it.
        /// </summary>
        /// <param name="height">Plate height, or 0 when the plate is hidden.</param>
        /// <returns>False when no nameplate draw was recorded recently, so the height is unknown.</returns>
        internal bool TryGetNamePlateHeight(out int height)
        {
            height = _namePlateHeight;

            return _namePlateTick != 0 && Time.Ticks - _namePlateTick <= OVERHEAD_STACK_STALE_MS;
        }

        /// <summary>
        /// World Y right above everything stacked over this mobile (sprite, simple healthbar, name, nameplate,
        /// HP percentage), where its overhead texts start.
        /// </summary>
        /// <param name="hitsShown">Whether the HP percentage is drawn above the nameplate.</param>
        /// <param name="bottomY">World Y for the bottom of the first overhead text.</param>
        /// <returns>False when the mobile was not drawn recently; fall back to its animation dimensions.</returns>
        private bool TryGetOverheadTextBottom(bool hitsShown, out int bottomY)
        {
            if (!TryGetOverheadStackTop(out bottomY))
            {
                return false;
            }

            // Plate and HP heights are screen pixels; scale them to the world units the texts are placed in.
            float zoom = Client.Game.Scene.Camera.Zoom;

            if (ObjectHandlesStatus == ObjectHandlesStatus.DISPLAYING)
            {
                int plateHeight = TryGetNamePlateHeight(out int measured) ? measured : Constants.OBJECT_HANDLES_GUMP_HEIGHT;

                if (plateHeight > 0)
                {
                    bottomY -= (int)((plateHeight + OVERHEAD_GAP) * zoom);
                }
            }

            if (hitsShown)
            {
                bool hitsScaleWithZoom = ProfileManager.CurrentProfile.OverheadsScaleWithZoom;
                int hitsHeight = HitsTexture.Height + OVERHEAD_GAP;
                bottomY -= hitsScaleWithZoom ? hitsHeight : (int)(hitsHeight * zoom);
            }

            return true;
        }

        // Extra vertical space to reserve above the overhead name/title so speech and
        // damage text drawn by other systems (Mobile.UpdateTextCoordsV, OverheadDamage)
        // doesn't overlap it.
        internal int NameOverheadTextExtraHeight =>
            ProfileManager.CurrentProfile != null && ProfileManager.CurrentProfile.ShowMobileNameOverhead && _nameText != null
                ? _nameText.Height + 5
                : 0;

        public override bool Draw(UltimaBatcher2D batcher, int posX, int posY, float depth)
        {
            if (IsDestroyed || !AllowedToDraw || !IsVisible)
            {
                return false;
            }

            // Check if player character should be hidden via Hide HUD system
            if (this is PlayerMobile player && !player.IsVisible)
            {
                return false;
            }

            Profile profile = _profile ?? Profile.DefaultPreviewProfile;
            Managers.AuraManager auraManager = World.AuraManager;
            int clientViewRange = World.ClientViewRange;

            bool charSitting = false;
            ushort overridenHue = 0;

            AnimationsLoader.SittingInfoData seatData = AnimationsLoader.SittingInfoData.Empty;
            _equipConvData = null;
            FrameInfo.X = 0;
            FrameInfo.Y = 0;
            FrameInfo.Width = 0;
            FrameInfo.Height = 0;

            posY -= 3;
            int drawX = posX + (int)Offset.X + 22;
            int drawY = posY + (int)(Offset.Y - Offset.Z) + 22;

            // Cache boolean values
            bool isDead = IsDead;
            bool isHidden = IsHidden;
            bool isPlayer = IsPlayer;
            bool hasShadow = !isDead && !isHidden && profile.ShadowsEnabled;
            bool inParty = InParty;
            bool isHuman = IsHuman;
            bool isGargoyle = IsGargoyle;
            bool isSelected = ReferenceEquals(SelectedObject.Object, this);

            if (auraManager.IsEnabled)
            {
                auraManager.Draw(
                    batcher,
                    drawX,
                    drawY,
                    profile.PartyAura && inParty
                        ? profile.PartyAuraHue
                        : Notoriety.GetHue(NotorietyFlag),
                    depth + 1f
                );
            }

            // ADVENTURE: inimigo marcado pelo auxiliar (pacote 0xBF 0xAD07): circulo vermelho no chao, por baixo do corpo
            if (Serial != 0 && Serial == ClassicUO.Game.UI.Gumps.AdventureInimigoGump.Uid)
            {
                ClassicUO.Game.UI.Gumps.AdventureInimigoGump.DesenhaAnel(batcher, drawX, drawY, depth + 1f);
            }

            float finalAlpha = isPlayer && profile.PlayerConstantAlpha != 100 ? profile.PlayerConstantAlpha / 100f : AlphaHue / 255f;
            Vector3 hueVec = ShaderHueTranslator.GetHueVector(0, false, finalAlpha);

            // Simplified hue override logic
            if (isSelected && profile.HighlightGameObjects)
            {
                overridenHue = Constants.HIGHLIGHT_CURRENT_OBJECT_HUE;
                hueVec.Y = 1;
            }
            else if (SelectedObject.HealthbarObject == this)
            {
                overridenHue = Notoriety.GetHue(NotorietyFlag);
            }
            else if (profile.NoColorObjectsOutOfRange && Distance > clientViewRange)
            {
                overridenHue = Constants.OUT_RANGE_COLOR;
                hueVec.Y = 1;
            }
            else if (World.Player != null && World.Player.IsDead && profile.EnableBlackWhiteEffect)
            {
                overridenHue = Constants.DEAD_RANGE_COLOR;
                hueVec.Y = 1;
            }
            else if (isHidden)
            {
                overridenHue = profile.HiddenBodyHue;
                hueVec = ShaderHueTranslator.GetHueVector(0, false, (float)profile.HiddenBodyAlpha / 100);
            }
            else
            {
                overridenHue = 0;

                if (isDead && !isHuman)
                {
                    overridenHue = 0x0386;
                }
                else if (!isDead)
                {
                    // Use single if-else chain instead of nested conditions
                    if (profile.HighlightMobilesByInvul && NotorietyFlag != NotorietyFlag.Invulnerable && IsYellowHits)
                    {
                        overridenHue = profile.InvulnerableHue;
                    }
                    else if (IsParalyzed && NotorietyFlag != NotorietyFlag.Invulnerable)
                    {
                        // ADVENTURE: paralisado = corpo de GELO (no lugar da cor laranja do cliente).
                        // Vale para qualquer paralyze (magia, field, etc.) e dura enquanto estiver paralisado.
                        if (AdvGeloAte < Time.Ticks + 300)
                        {
                            AdvGeloAte = Time.Ticks + 300;
                        }
                    }
                    else if (profile.HighlightMobilesByPoisoned && IsPoisoned)
                    {
                        overridenHue = profile.PoisonHue;
                    }
                }
            }

            if (!isPlayer)
            {
                Managers.TargetManager targetManager = World.TargetManager;
                if ((targetManager.IsTargeting && isSelected) || (Serial == targetManager.LastAttack && !profile.DisableGrayEnemies))
                    overridenHue = Notoriety.GetHue(NotorietyFlag);
                else if (inParty && profile.OverridePartyAndGuildHue)
                    overridenHue = profile.FriendHue;
            }

            ProcessSteps(out byte dir);
            byte layerDir = dir;

            Client.Game.UO.Animations.GetAnimDirection(ref dir, ref IsFlipped);

            ushort graphic = GetGraphicForAnimation();
            byte animGroup = GetGroupForAnimation(this, graphic, true);
            byte animIndex = AnimIndex;

            Item mount = Mount;
            if (mount != null)
            {
                // Validate mount still exists - don't modify state during rendering
                if (World.Items.Get(mount.Serial) == null)
                {
                    mount = null;
                }
            }

            sbyte mountOffsetY = 0;

            if (isHuman && !isDead && mount != null && mount.Graphic != 0x3E96)
            {
                ushort mountGraphic = mount.GetGraphicForAnimation();
                byte animGroupMount = 0;

                if (
                    mountGraphic != 0xFFFF
                    && mountGraphic < Client.Game.UO.Animations.MaxAnimationCount
                )
                {
                    bool drawMountAsSingleLayer = false;
                    if (Mounts.TryGet(mount.Graphic, out MountInfo mountInfo))
                    {
                        mountOffsetY = mountInfo.OffsetY;
                        drawMountAsSingleLayer = mountInfo.DrawAsSingleLayer;
                    }

                    // Calculate animation group once
                    animGroupMount = GetGroupForAnimation(this, mountGraphic);

                    if (hasShadow)
                    {
                        DrawInternal(
                            batcher,
                            this,
                            null,
                            drawX,
                            drawY + 10,
                            hueVec,
                            IsFlipped,
                            animIndex,
                            true,
                            graphic,
                            animGroup,
                            dir,
                            isHuman,
                            false,
                            false,
                            false,
                            depth,
                            mountOffsetY,
                            overridenHue,
                            charSitting
                        );

                        DrawInternal(
                            batcher,
                            this,
                            mount,
                            drawX,
                            drawY,
                            hueVec,
                            IsFlipped,
                            animIndex,
                            true,
                            mountGraphic,
                            animGroupMount,
                            dir,
                            isHuman,
                            false,
                            false,
                            false,
                            depth,
                            mountOffsetY,
                            overridenHue,
                            charSitting
                        );
                    }

                    DrawInternal(
                        batcher,
                        this,
                        mount,
                        drawX,
                        drawY,
                        hueVec,
                        IsFlipped,
                        animIndex,
                        false,
                        mountGraphic,
                        animGroupMount,
                        dir,
                        isHuman,
                        false,
                        true,
                        false,
                        depth,
                        mountOffsetY,
                        overridenHue,
                        charSitting,
                        drawAsSingleLayer: drawMountAsSingleLayer
                    );

                    drawY += mountOffsetY;
                }
            }
            else
            {
                if (TryGetSittingInfo(out seatData))
                {
                    animGroup = (byte)PeopleAnimationGroup.Stand;
                    animIndex = 0;

                    ProcessSteps(out dir);

                    Client.Game.UO.FileManager.Animations.FixSittingDirection(
                        ref dir,
                        ref IsFlipped,
                        ref drawX,
                        ref drawY,
                        ref seatData
                    );

                    drawY += SIT_OFFSET_Y;

                    if (dir == 3)
                    {
                        if (isGargoyle)
                        {
                            drawY -= 30 - SIT_OFFSET_Y;
                            animGroup = 42;
                        }
                        else
                        {
                            animGroup = 25;
                        }
                    }
                    else if (isGargoyle)
                    {
                        animGroup = 42;
                    }
                    else
                    {
                        charSitting = true;
                    }
                }
                else if (hasShadow)
                {
                    DrawInternal(
                        batcher,
                        this,
                        null,
                        drawX,
                        drawY,
                        hueVec,
                        IsFlipped,
                        animIndex,
                        true,
                        graphic,
                        animGroup,
                        dir,
                        isHuman,
                        false,
                        false,
                        false,
                        depth,
                        mountOffsetY,
                        overridenHue,
                        charSitting
                    );
                }
            }

            DrawInternal(
                batcher,
                this,
                null,
                drawX,
                drawY,
                hueVec,
                IsFlipped,
                animIndex,
                false,
                graphic,
                animGroup,
                dir,
                isHuman,
                false,
                false,
                isGargoyle,
                depth,
                mountOffsetY,
                overridenHue,
                charSitting,
                outlineColor: OutlineColor
            );

            if (!IsEmpty)
            {
                for (int i = 0; i < Constants.USED_LAYER_COUNT; i++)
                {
                    Layer layer = LayerOrder.UsedLayers[layerDir, i];

                    Item item = FindItemByLayer(layer);

                    if (item == null)
                    {
                        continue;
                    }

                    if (IsDead && (layer == Layer.Hair || layer == Layer.Beard))
                    {
                        continue;
                    }

                    if (isHuman)
                    {
                        bool hideLayersForSelf = profile.HideLayersForSelf;

                        if (profile.HiddenLayersEnabled && profile.HiddenLayers.Contains((int)layer) && ((hideLayersForSelf && IsPlayer) || !hideLayersForSelf))
                        {
                            continue;
                        }

                        if (IsCovered(this, layer))
                        {
                            continue;
                        }

                        if (item.ItemData.AnimID != 0)
                        {
                            graphic = item.ItemData.AnimID;

                            if (isGargoyle)
                            {
                                FixGargoyleEquipments(ref graphic);
                            }

                            if (
                                Client.Game.UO.FileManager.Animations.EquipConversions.TryGetValue(
                                    Graphic,
                                    out Dictionary<ushort, EquipConvData> map
                                )
                            )
                            {
                                if (map.TryGetValue(item.ItemData.AnimID, out EquipConvData data))
                                {
                                    _equipConvData = data;
                                    graphic = data.Graphic;
                                }
                            }

                            byte group = isGargoyle /*&& item.ItemData.IsWeapon*/
                                        && seatData.Graphic == 0
                                ? GetGroupForAnimation(this, graphic, true)
                                : animGroup;

                            DrawInternal(
                                batcher,
                                this,
                                item,
                                drawX,
                                drawY,
                                hueVec,
                                IsFlipped,
                                animIndex,
                                false,
                                graphic,
                                group,
                                dir,
                                isHuman,
                                true,
                                false,
                                isGargoyle,
                                depth,
                                mountOffsetY,
                                overridenHue,
                                // The sitting squash folds the legs; held weapons must keep their shape.
                                layer != Layer.OneHanded && layer != Layer.TwoHanded && charSitting,
                                outlineColor: OutlineColor
                            );

                            if (layer == Layer.Robe && Settings.GlobalSettings.CustomServer == Settings.CustomServers.Eventine)
                            {
                                // Search for item with graphic 0xA413
                                Item aboveRobe = GetItemByGraphic(0xA413);

                                if (aboveRobe != null)
                                {
                                    ushort specialGraphic = aboveRobe.ItemData.AnimID != 0 ? aboveRobe.ItemData.AnimID : aboveRobe.Graphic;
                                    if (isGargoyle)
                                        FixGargoyleEquipments(ref specialGraphic);
                                    byte specialGroup = isGargoyle ? GetGroupForAnimation(this, specialGraphic, true) : animGroup;
                                    DrawInternal(
                                        batcher,
                                        this,
                                        aboveRobe,
                                        drawX,
                                        drawY,
                                        hueVec,
                                        IsFlipped,
                                        animIndex,
                                        false,
                                        specialGraphic,
                                        specialGroup,
                                        dir,
                                        isHuman,
                                        true,
                                        false,
                                        isGargoyle,
                                        depth,
                                        mountOffsetY,
                                        overridenHue,
                                        charSitting,
                                        outlineColor: OutlineColor
                                    );
                                }
                            }
                        }
                        else
                        {
                            if (item.ItemData.IsLight)
                            {
                                GameScene.Instance?.AddLight(this, item, drawX, drawY);
                            }
                        }

                        _equipConvData = null;
                    }
                    else
                    {
                        if (item.ItemData.IsLight)
                        {
                            GameScene.Instance?.AddLight(this, item, drawX, drawY);
                        }
                    }
                }
            }
            
            // ADVENTURE: EFEITOS NO CORPO (pacote 0xBF 0xAD04). O proprio desenho do personagem e
            // redesenhado varias vezes por cima, tingido, maior e mais alto a cada copia.
            //   Fogo (Flamestrike) = labaredas vivas subindo (vermelho -> laranja -> amarelo), luz somada
            //   Maldicao (Curse)   = corpo escurecido + fumaca roxa subindo devagar
            //   Gelo (Paralyze)    = corpo congelado azul-gelo brilhando, parado
            // So visual; dura o tempo que o servidor mandou.
            if (AdvFogoAte != 0 || AdvMaldicaoAte != 0 || AdvGeloAte != 0 || AdvEscudoAte != 0)
            {
                float advEscalaOriginal = Scale;
                Rectangle advFrameOriginal = FrameInfo;   // nao mexe na area de clique
                double advT = Time.Ticks / 1000.0;

                void AdvCamadas(ushort[] hues, float alfaIni, float alfaPasso, float escPasso, int sobePasso, int treme, float sumir, bool aditivo, double velocidade, int lados = 0)
                {
                    if (aditivo)
                    {
                        batcher.SetBlendState(BlendState.Additive);   // luz SOMADA: cor viva em vez de bege
                    }

                    for (int k = 0; k < hues.Length; k++)
                    {
                        int dx = treme == 0 ? 0 : (int)(Math.Sin(advT * 17 * velocidade + k * 1.9) * (treme + k / 3));
                        int sobe = k * sobePasso + (sobePasso == 0 ? 0 : (int)(Math.Abs(Math.Sin(advT * 9 * velocidade + k)) * 6));
                        float alfa = Math.Max(0f, (alfaIni - k * alfaPasso) * sumir);
                        Scale = advEscalaOriginal * (1f + k * escPasso + (escPasso == 0 ? 0f : (float)Math.Sin(advT * 11 * velocidade + k) * 0.04f));

                        DrawInternal(batcher, this, null, drawX + dx, drawY - sobe, ShaderHueTranslator.GetHueVector(0, false, alfa),
                            IsFlipped, animIndex, false, graphic, animGroup, dir, isHuman, false, false, isGargoyle,
                            depth + 2f + k * 0.01f, mountOffsetY, hues[k], charSitting);

                        // labaredas para os LADOS (da volume): copias deslocadas para a esquerda e direita
                        if (lados > 0 && k >= 2)
                        {
                            int abre = lados + k * 3 + (int)(Math.Sin(advT * 13 * velocidade + k * 2.3) * 3);
                            Vector3 hLado = ShaderHueTranslator.GetHueVector(0, false, alfa * 0.65f);

                            DrawInternal(batcher, this, null, drawX + dx - abre, drawY - sobe + k, hLado,
                                IsFlipped, animIndex, false, graphic, animGroup, dir, isHuman, false, false, isGargoyle,
                                depth + 2f + k * 0.01f, mountOffsetY, hues[k], charSitting);
                            DrawInternal(batcher, this, null, drawX + dx + abre, drawY - sobe + k, hLado,
                                IsFlipped, animIndex, false, graphic, animGroup, dir, isHuman, false, false, isGargoyle,
                                depth + 2f + k * 0.01f, mountOffsetY, hues[k], charSitting);
                        }
                    }

                    if (aditivo)
                    {
                        batcher.SetBlendState(null);
                    }

                    Scale = advEscalaOriginal;
                }

                // ---- MALDICAO (Curse): escurece e solta fumaca roxa ----
                if (AdvMaldicaoAte != 0)
                {
                    if (Time.Ticks >= AdvMaldicaoAte) { AdvMaldicaoAte = 0; }
                    else
                    {
                        float sumir = Math.Min(1f, (AdvMaldicaoAte - Time.Ticks) / 500f);
                        AdvCamadas(AdvHuesEscuro, 0.55f, 0f, 0f, 0, 0, sumir, false, 1);                         // corpo escuro
                        AdvCamadas(AdvHuesFumaca, 0.30f, 0.05f, 0.10f, 9, 2, sumir, true, 0.4); // fumaca roxa
                    }
                }

                // ---- GELO (Paralyze): congelado, brilho azul parado ----
                if (AdvGeloAte != 0)
                {
                    if (Time.Ticks >= AdvGeloAte) { AdvGeloAte = 0; }
                    else
                    {
                        float sumir = Math.Min(1f, (AdvGeloAte - Time.Ticks) / 400f);
                        float pisca = 0.5f + 0.5f * (float)Math.Sin(advT * 9);   // pisca forte: identifica quem esta paralisado
                        AdvCamadas(AdvHuesGeloPinta, 0.80f, 0f, 0f, 0, 0, sumir, false, 1);          // pinta o corpo de gelo
                        AdvCamadas(AdvHuesGelo, 0.65f * pisca, 0.2f, 0.05f, 0, 0, sumir, true, 1);   // brilho branco piscando
                    }
                }

                // ---- AURA (Protection): copias do corpo em luz azul, cada uma MAIOR, respirando ----
                // No comeco a aura "explode" bem larga e depois fica pulsando em volta do corpo.
                if (AdvEscudoAte != 0)
                {
                    if (Time.Ticks >= AdvEscudoAte) { AdvEscudoAte = 0; }
                    else
                    {
                        float sumir = Math.Min(1f, (AdvEscudoAte - Time.Ticks) / 600f);
                        float desde = (Time.Ticks - AdvEscudoInicio) / 1000f;
                        float explode = desde < 0.5f ? 1f - desde / 0.5f : 0f;           // 1 -> 0 no primeiro meio segundo
                        float respira = 0.5f + 0.5f * (float)Math.Sin(advT * 5);          // pulsa
                        float abre = 0.06f + 0.05f * respira + 0.22f * explode;          // amplitude: quanto cada camada cresce
                        AdvCamadas(AdvHuesEscudo, 0.42f + 0.25f * explode, 0.06f, abre, 0, 0, sumir, true, 0.6);
                    }
                }

                // ---- FOGO (Flamestrike): labaredas vivas subindo ----
                if (AdvFogoAte != 0)
                {
                    if (Time.Ticks >= AdvFogoAte) { AdvFogoAte = 0; }
                    else
                    {
                        float sumir = Math.Min(1f, (AdvFogoAte - Time.Ticks) / 400f);
                        // de dentro pra fora: vermelho forte -> laranja -> amarelo nas pontas
                        AdvCamadas(AdvHuesFogo, 0.60f, 0.045f, 0.14f, 8, 3, sumir, true, 1, 6);   // 6 = abre para os lados
                    }
                }

                Scale = advEscalaOriginal;
                FrameInfo = advFrameOriginal;
            }

            // drawY + FrameInfo.Y is the top of the drawn sprite; the simple healthbar and name stack on it.
            uint lookKey = ((uint)graphic << 16) | (mount != null ? mount.Graphic : 0u);
            bool isActionFrame = AnimationFromServer && _animationGroup != 0xFF;
            int spriteTopY = drawY - _overheadStackAnchor.Update(Math.Max(0, -FrameInfo.Y), lookKey, !isActionFrame);

            if (profile.ShowMobileHealthbar)
            {
                spriteTopY -= 5;
                DrawHealthbar(batcher, drawX, spriteTopY, depth);
            }

            if (profile.ShowMobileNameOverhead)
            {
                if (_nameText == null)
                {
                    string name = Name;

                    if (World.OPL.TryGetNameAndData(this, out string oplname, out string opldata))
                        if (oplname.NotNullNotEmpty())
                            name = oplname;

                    if(name.NotNullNotEmpty())
                        _nameText = TextBox.GetOne(
                            name, 
                            profile.OverheadChatFont, 
                            profile.OverheadChatFontSize, 
                            Notoriety.GetHue(NotorietyFlag), 
                            TextBox.RTLOptions.DefaultCenterStroked(profile.OverheadChatWidth));
                }

                if (_nameText != null)
                {
                    _nameText.Draw(batcher, drawX - (_nameText.Width >> 1), spriteTopY - _nameText.Height, depth);
                    spriteTopY -= _nameText.Height;
                }
            }

            _overheadStackTopOffset = spriteTopY - (RealScreenPosition.Y + (int)(Offset.Y - Offset.Z));
            _overheadStackTick = Time.Ticks;
            _hasOverheadStack = true;

            FrameInfo.X = Math.Abs(FrameInfo.X);
            FrameInfo.Y = Math.Abs(FrameInfo.Y);
            FrameInfo.Width = FrameInfo.X + FrameInfo.Width;
            FrameInfo.Height = FrameInfo.Y + FrameInfo.Height;
            return true;
        }

        private const int HEALTHBAR_WIDTH = 60;
        private const int HEALTHBAR_HEIGHT = 5;

        private void DrawHealthbar(UltimaBatcher2D batcher, int x, int y, float depth)
        {
            // Centered horizontally on the sprite, placed above the sprite.
            int barX = x - (HEALTHBAR_WIDTH / 2);
            int barY = y;

            // Match the character's own draw depth so the bar sorts together with the mobile.
            float barDepth = depth + 1f;

            // Background
            batcher.Draw(
                SolidColorTextureCache.GetTexture(Color.Black),
                new Rectangle(barX, barY, HEALTHBAR_WIDTH, HEALTHBAR_HEIGHT),
                ShaderHueTranslator.GetHueVector(0, false, 1f),
                barDepth
            );

            float hitPercentage = HitsMax > 0 ? (float)Hits / HitsMax : 1f;
            hitPercentage = Math.Clamp(hitPercentage, 0f, 1f);

            int fillWidth = (int)((HEALTHBAR_WIDTH - 2) * hitPercentage);

            if (fillWidth > 0)
            {
                ushort barHue;

                if (IsPoisoned)
                {
                    barHue = 63;
                }
                else if (IsYellowHits)
                {
                    barHue = 53;
                }
                else
                {
                    barHue = Notoriety.GetHue(NotorietyFlag);
                }

                batcher.Draw(
                    SolidColorTextureCache.GetTexture(Color.White),
                    new Rectangle(barX + 1, barY + 1, fillWidth, HEALTHBAR_HEIGHT - 2),
                    ShaderHueTranslator.GetHueVector(barHue, false, 1f),
                    barDepth + 0.01f
                );
            }
        }

        private ushort GetAnimationInfo(Item item, bool isGargoyle)
        {
            if (item.ItemData.AnimID != 0)
            {
                ushort graphic = item.ItemData.AnimID;

                if (isGargoyle)
                {
                    FixGargoyleEquipments(ref graphic);
                }

                if (
                    Client.Game.UO.FileManager.Animations.EquipConversions.TryGetValue(
                        Graphic,
                        out Dictionary<ushort, EquipConvData> map
                    )
                )
                {
                    if (map.TryGetValue(item.ItemData.AnimID, out EquipConvData data))
                    {
                        _equipConvData = data;
                        graphic = data.Graphic;
                    }
                }

                return graphic;
            }

            return 0xFFFF;
        }

        private static void FixGargoyleEquipments(ref ushort graphic)
        {
            switch (graphic)
            {
                // gargoyle robe
                case 0x01D5:
                    graphic = 0x0156;

                    break;

                // gargoyle dead shroud
                case 0x03CA:
                    graphic = 0x0223;

                    break;

                // gargoyle spellbook
                case 0x03D8:
                    graphic = 329;

                    break;

                // gargoyle necrobook
                case 0x0372:
                    graphic = 330;

                    break;

                // gargoyle chivalry book
                case 0x0374:
                    graphic = 328;

                    break;

                // gargoyle bushido book
                case 0x036F:
                    graphic = 327;

                    break;

                // gargoyle ninjitsu book
                case 0x036E:
                    graphic = 328;

                    break;

                // gargoyle masteries book
                case 0x0426:
                    graphic = 0x042B;

                    break;
                //NOTE: gargoyle mysticism book seems ok. Mha!


                /* into the mobtypes.txt file of 7.0.90+ client version we have:
                 *
                 *   1529 	EQUIPMENT	0		# EQUIP_Shield_Pirate_Male_H
                 *   1530 	EQUIPMENT	0		# EQUIP_Shield_Pirate_Female_H
                 *   1531 	EQUIPMENT	10000	# Equip_Shield_Pirate_Male_G
                 *   1532 	EQUIPMENT	10000	# Equip_Shield_Pirate_Female_G
                 *
                 *   This means that graphic 0xA649 [pirate shield] has 4 tiledata infos.
                 *   Standard client handles it automatically without any issue.
                 *   Maybe it's hardcoded into the client
                 */

                // EQUIP_Shield_Pirate_Male_H
                case 1529:
                    graphic = 1531;

                    break;

                // EQUIP_Shield_Pirate_Female_H
                case 1530:
                    graphic = 1532;

                    break;
            }
        }

        private static bool GetTexture(
            ushort graphic,
            byte animGroup,
            ref byte animIndex,
            byte direction,
            out SpriteInfo spriteInfo,
            out bool isUOP
        )
        {
            spriteInfo = default;

            Span<SpriteInfo> frames = Client.Game.UO.Animations.GetAnimationFrames(
                graphic,
                animGroup,
                direction,
                out _,
                out isUOP
            );

            if (frames.Length == 0)
            {
                return false;
            }

            if (animIndex < 0)
            {
                animIndex = 0;
            }

            animIndex = (byte)(animIndex % frames.Length);

            spriteInfo = frames[animIndex];

            if (spriteInfo.Texture == null)
            {
                return false;
            }

            return true;
        }

        private static void DrawInternal(
            UltimaBatcher2D batcher,
            Mobile owner,
            Item entity,
            int x,
            int y,
            Vector3 hueVec,
            bool mirror,
            byte frameIndex,
            bool hasShadow,
            ushort id,
            byte animGroup,
            byte dir,
            bool isHuman,
            bool isEquip,
            bool isMount,
            bool forceUOP,
            float depth,
            sbyte mountOffset,
            ushort overridedHue,
            bool charIsSitting,
            bool drawAsSingleLayer = false,
            Color? outlineColor = null
        )
        {
            if (id >= Client.Game.UO.Animations.MaxAnimationCount || owner == null)
            {
                return;
            }

            Span<SpriteInfo> frames = Client.Game.UO.Animations.GetAnimationFrames(
                id,
                animGroup,
                dir,
                out ushort hueFromFile,
                out _,
                isEquip,
                false
            );

            if (hueFromFile == 0)
            {
                hueFromFile = overridedHue;
            }

            if (frames.Length == 0)
            {
                if (entity != null && entity.ItemData.IsLight) GameScene.Instance?.AddLight(owner, entity, x, y);
                return;
            }

            if (frameIndex >= frames.Length)
            {
                frameIndex = (byte)(frames.Length - 1);
            }
            else if (frameIndex < 0)
            {
                frameIndex = 0;
            }

            ref SpriteInfo spriteInfo = ref frames[frameIndex % frames.Length];

            if (spriteInfo.Texture == null)
            {
                // Only continue for sitting characters without entity and no shadow
                if (!(charIsSitting && entity == null && !hasShadow))
                {
                    return;
                }
                // Skip position calculations, go directly to shadow check
            }
            else
            {
                // Position calculations only when texture exists
                if (mirror)
                {
                    x -= (int)((spriteInfo.UV.Width - spriteInfo.Center.X) * owner.Scale);
                }
                else
                {
                    x -= (int)(spriteInfo.Center.X * owner.Scale);
                }

                y -= (int)((spriteInfo.UV.Height + spriteInfo.Center.Y) * owner.Scale);
            }

            if (hasShadow)
            {
                batcher.DrawShadow(
                    spriteInfo.Texture,
                    new Vector2(x, y),
                    spriteInfo.UV,
                    mirror,
                    depth
                );
            }
            else
            {
                ushort hue = overridedHue;
                bool partialHue = false;

                if (hue == 0)
                {
                    hue = entity?.Hue ?? owner.Hue;
                    partialHue = !isMount && entity != null && entity.ItemData.IsPartialHue;

                    if ((hue & 0x8000) != 0)
                    {
                        partialHue = true;
                        hue &= 0x7FFF;
                    }

                    if (hue == 0)
                    {
                        hue = hueFromFile;

                        if (hue == 0 && owner._equipConvData is EquipConvData equipData)
                        {
                            hue = equipData.Color;
                        }

                        partialHue = false;
                    }
                }

                hueVec = ShaderHueTranslator.GetHueVector(hue, partialHue, hueVec.Z);

                if (spriteInfo.Texture != null)
                {
                    var pos = new Vector2(x, y);
                    Rectangle rect = spriteInfo.UV;

                    if (charIsSitting)
                    {
                        Vector3 mod = owner.CalculateSitAnimation(y, entity, isHuman, ref spriteInfo);

                        batcher.DrawCharacterSitted(
                            spriteInfo.Texture,
                            pos,
                            rect,
                            mod,
                            hueVec,
                            mirror,
                            depth + 1f
                        );
                    }
                    else
                    {
                        if (isMount && drawAsSingleLayer)
                        {
                            batcher.Draw(
                                spriteInfo.Texture,
                                pos,
                                rect,
                                hueVec,
                                0f,
                                Vector2.Zero,
                                owner.Scale,
                                mirror ? SpriteEffects.FlipHorizontally : SpriteEffects.None,
                                depth + 1f
                            );
                        }
                        else
                        {
                            int diffY = (spriteInfo.UV.Height + spriteInfo.Center.Y) - mountOffset;

                            int value = Math.Max(1, diffY);
                            int count = Math.Max((spriteInfo.UV.Height / value) + 1, 2);

                            rect.Height = Math.Min(value, rect.Height);
                            int remains = spriteInfo.UV.Height - rect.Height;

                            // Depth step between vertical slices of the character. Lower values
                            // reduce how far the feet are pushed forward in the depth buffer, so
                            // they clip through fewer walls the mobile is standing behind.
                            int tiles = ProfileManager.CurrentProfile?.MobileDepthSliceStep ?? 2;

                            for (int i = 0; i < count; ++i)
                            {
                                batcher.Draw(
                                    spriteInfo.Texture,
                                    pos,
                                    rect,
                                    hueVec,
                                    0f,
                                    Vector2.Zero,
                                    owner.Scale,
                                    mirror ? SpriteEffects.FlipHorizontally : SpriteEffects.None,
                                    depth + 1f + (i * tiles)
                                );

                                pos.Y += rect.Height * owner.Scale;
                                rect.Y += rect.Height;
                                rect.Height = remains;
                                remains -= rect.Height;
                            }
                        }

                        if (outlineColor.HasValue)
                        {
                            Color oc = outlineColor.Value;
                            var outlineNormal = new Vector3(oc.R / 255f, oc.G / 255f, oc.B / 255f);
                            Vector3 outlineHue = ShaderHueTranslator.GetOutlineHueVector(hueVec.Z);

                            batcher.DrawOutlined(
                                spriteInfo.Texture,
                                new Vector2(x, y),
                                spriteInfo.UV,
                                outlineHue,
                                outlineNormal,
                                0f,
                                Vector2.Zero,
                                owner.Scale,
                                mirror ? SpriteEffects.FlipHorizontally : SpriteEffects.None,
                                depth + 0.999f
                            );
                        }
                    }

                    int xx = (int)(-spriteInfo.Center.X * owner.Scale);
                    int yy = (int)(-(spriteInfo.UV.Height + spriteInfo.Center.Y + 3) * owner.Scale);

                    if (mirror)
                    {
                        xx = (int)(-(spriteInfo.UV.Width - spriteInfo.Center.X) * owner.Scale);
                    }

                    if (xx < owner.FrameInfo.X)
                    {
                        owner.FrameInfo.X = xx;
                    }

                    if (yy < owner.FrameInfo.Y)
                    {
                        owner.FrameInfo.Y = yy;
                    }

                    if (owner.FrameInfo.Width < xx + (int)(spriteInfo.UV.Width * owner.Scale))
                    {
                        owner.FrameInfo.Width = xx + (int)(spriteInfo.UV.Width * owner.Scale);
                    }

                    if (owner.FrameInfo.Height < yy + (int)(spriteInfo.UV.Height * owner.Scale))
                    {
                        owner.FrameInfo.Height = yy + (int)(spriteInfo.UV.Height * owner.Scale);
                    }
                }

                if (entity != null && entity.ItemData.IsLight)
                {
                    GameScene.Instance?.AddLight(owner, entity, mirror ? x + spriteInfo.UV.Width : x, y);
                }
            }
        }

        private Vector3 CalculateSitAnimation(
            int y,
            Item entity,
            bool isHuman,
            ref SpriteInfo spriteInfo
        )
        {
            var mod = new Vector3();

            const float UPPER_BODY_RATIO = 0.35f;
            const float MID_BODY_RATIO = 0.60f;
            const float LOWER_BODY_RATIO = 0.94f;

            if (entity == null && isHuman)
            {
                int frameHeight = spriteInfo.UV.Height;
                if (frameHeight == 0)
                {
                    frameHeight = 61;
                }

                _characterFrameStartY = y - (spriteInfo.Texture != null ? 0 : frameHeight - SIT_OFFSET_Y);
                _startCharacterWaistY = (int)(frameHeight * UPPER_BODY_RATIO) + _characterFrameStartY;
                _startCharacterKneesY = (int)(frameHeight * MID_BODY_RATIO) + _characterFrameStartY;

                if (spriteInfo.Texture == null)
                {
                    return mod;
                }
            }

            mod.X = UPPER_BODY_RATIO;
            mod.Y = MID_BODY_RATIO;
            mod.Z = LOWER_BODY_RATIO;

            if (entity != null)
            {
                float itemsEndY = y + spriteInfo.UV.Height;

                if (y >= _startCharacterWaistY)
                {
                    mod.X = 0;
                }
                else if (itemsEndY <= _startCharacterWaistY)
                {
                    mod.X = 1.0f;
                }
                else
                {
                    float upperBodyDiff = _startCharacterWaistY - y;
                    mod.X = upperBodyDiff / spriteInfo.UV.Height;

                    if (mod.X < 0)
                    {
                        mod.X = 0;
                    }
                }

                if (_startCharacterWaistY >= itemsEndY || y >= _startCharacterKneesY)
                {
                    mod.Y = 0;
                }
                else if (_startCharacterWaistY <= y && itemsEndY <= _startCharacterKneesY)
                {
                    mod.Y = 1.0f;
                }
                else
                {
                    float midBodyDiff;

                    if (y >= _startCharacterWaistY)
                    {
                        midBodyDiff = _startCharacterKneesY - y;
                    }
                    else if (itemsEndY <= _startCharacterKneesY)
                    {
                        midBodyDiff = itemsEndY - _startCharacterWaistY;
                    }
                    else
                    {
                        midBodyDiff = _startCharacterKneesY - _startCharacterWaistY;
                    }

                    mod.Y = mod.X + midBodyDiff / spriteInfo.UV.Height;

                    if (mod.Y < 0)
                    {
                        mod.Y = 0;
                    }
                }

                if (itemsEndY <= _startCharacterKneesY)
                {
                    mod.Z = 0;
                }
                else if (y >= _startCharacterKneesY)
                {
                    mod.Z = 1.0f;
                }
                else
                {
                    float lowerBodyDiff = itemsEndY - _startCharacterKneesY;
                    mod.Z = mod.Y + lowerBodyDiff / spriteInfo.UV.Height;

                    if (mod.Z < 0)
                    {
                        mod.Z = 0;
                    }
                }
            }

            return mod;
        }

        public override bool CheckMouseSelection()
        {
            Point position = RealScreenPosition;
            position.Y -= 3;
            position.X += (int)Offset.X + 22;
            position.Y += (int)(Offset.Y - Offset.Z) + 22;

            Rectangle r = FrameInfo;
            r.X = position.X - r.X;
            r.Y = position.Y - r.Y;

            if (!r.Contains(SelectedObject.TranslatedMousePositionByViewport))
            {
                return false;
            }

            bool isHuman = IsHuman;
            bool isGargoyle =
                Client.Game.UO.Version >= ClientVersion.CV_7000
                && (Graphic == 666 || Graphic == 667 || Graphic == 0x02B7 || Graphic == 0x02B6);

            Renderer.Animations.Animations animations = Client.Game.UO.Animations;

            ProcessSteps(out byte dir);
            byte layerDir = dir;
            bool isFlipped = IsFlipped;
            animations.GetAnimDirection(ref dir, ref isFlipped);

            ushort graphic = GetGraphicForAnimation();
            byte animGroup = GetGroupForAnimation(this, graphic, true);
            byte animIndex = AnimIndex;

            byte animGroupBackup = animGroup;
            byte animIndexBackup = animIndex;

            SpriteInfo spriteInfo;
            bool isUop;

            if (isHuman && !IsDead)
            {
                Item mount = Mount;
                if (mount != null)
                {
                    ushort mountGraphic = mount.GetGraphicForAnimation();

                    if (mountGraphic != 0xFFFF)
                    {
                        byte animGroupMount = GetGroupForAnimation(this, mountGraphic);

                        if (
                            GetTexture(
                                mountGraphic,
                                animGroupMount,
                                ref animIndex,
                                dir,
                                out spriteInfo,
                                out isUop
                            )
                        )
                        {
                            int x =
                                position.X
                                - (int)((
                                    isFlipped
                                        ? spriteInfo.UV.Width - spriteInfo.Center.X
                                        : spriteInfo.Center.X
                                ) * Scale);
                            int y = position.Y - (int)((spriteInfo.UV.Height + spriteInfo.Center.Y) * Scale);

                            if (
                                animations.PixelCheck(
                                    mountGraphic,
                                    animGroupMount,
                                    dir,
                                    isUop,
                                    animIndex,
                                    (int)((isFlipped
                                        ? x
                                            + spriteInfo.UV.Width * Scale
                                            - SelectedObject.TranslatedMousePositionByViewport.X
                                        : SelectedObject.TranslatedMousePositionByViewport.X - x) / Scale),
                                    (int)((SelectedObject.TranslatedMousePositionByViewport.Y - y) / Scale)
                                )
                            )
                            {
                                return true;
                            }

                            if (Mounts.TryGet(mount.Graphic, out MountInfo moutInfo))
                            {
                                position.Y += moutInfo.OffsetY;
                            }
                        }
                    }
                }
            }

            if (GetTexture(graphic, animGroup, ref animIndex, dir, out spriteInfo, out isUop))
            {
                int x =
                    position.X
                    - (int)((isFlipped ? spriteInfo.UV.Width - spriteInfo.Center.X : spriteInfo.Center.X) * Scale);
                int y = position.Y - (int)((spriteInfo.UV.Height + spriteInfo.Center.Y) * Scale);

                if (
                    animations.PixelCheck(
                        graphic,
                        animGroup,
                        dir,
                        isUop,
                        animIndex,
                        (int)((isFlipped
                            ? x
                                + spriteInfo.UV.Width * Scale
                                - SelectedObject.TranslatedMousePositionByViewport.X
                            : SelectedObject.TranslatedMousePositionByViewport.X - x) / Scale),
                        (int)((SelectedObject.TranslatedMousePositionByViewport.Y - y) / Scale)
                    )
                )
                {
                    return true;
                }
            }

            if (!IsEmpty && isHuman)
            {
                for (int i = 0; i < Constants.USED_LAYER_COUNT; i++)
                {
                    Layer layer = LayerOrder.UsedLayers[layerDir, i];
                    Item item = FindItemByLayer(layer);

                    if (
                        item == null
                        || (IsDead && (layer == Layer.Hair || layer == Layer.Beard))
                        || IsCovered(this, layer)
                    )
                    {
                        continue;
                    }

                    graphic = GetAnimationInfo(item, isGargoyle);

                    if (graphic != 0xFFFF)
                    {
                        animGroup = animGroupBackup;
                        animIndex = animIndexBackup;

                        if (
                            GetTexture(
                                graphic,
                                animGroup,
                                ref animIndex,
                                dir,
                                out spriteInfo,
                                out isUop
                            )
                        )
                        {
                            int x =
                                position.X
                                - (int)((
                                    isFlipped
                                        ? spriteInfo.UV.Width - spriteInfo.Center.X
                                        : spriteInfo.Center.X
                                ) * Scale);
                            int y = position.Y - (int)((spriteInfo.UV.Height + spriteInfo.Center.Y) * Scale);

                            if (
                                animations.PixelCheck(
                                    graphic,
                                    animGroup,
                                    dir,
                                    isUop,
                                    animIndex,
                                    (int)((isFlipped
                                        ? x
                                            + spriteInfo.UV.Width * Scale
                                            - SelectedObject.TranslatedMousePositionByViewport.X
                                        : SelectedObject.TranslatedMousePositionByViewport.X - x) / Scale),
                                    (int)((SelectedObject.TranslatedMousePositionByViewport.Y - y) / Scale)
                                )
                            )
                            {
                                return true;
                            }
                        }
                    }
                }
            }

            return false;
        }

        internal static bool IsCovered(Mobile mobile, Layer layer)
        {
            if (mobile.IsEmpty)
            {
                return false;
            }

            switch (layer)
            {
                case Layer.Shoes:
                    Item pants = mobile.FindItemByLayer(Layer.Pants);
                    Item robe;

                    //Eventine ignores pants layers
                    if ((Settings.GlobalSettings.CustomServer != Settings.CustomServers.Eventine && mobile.FindItemByLayer(Layer.Legs) != null)
                        || pants != null
                        && (
                            pants.Graphic == 0x1411 /*|| pants.Graphic == 0x141A*/
                        )
                    )
                    {
                        return true;
                    }
                    else
                    {
                        robe = mobile.FindItemByLayer(Layer.Robe);

                        if (
                            pants != null && (pants.Graphic == 0x0513 || pants.Graphic == 0x0514)
                            || robe != null && robe.Graphic == 0x0504
                        )
                        {
                            return true;
                        }
                    }

                    break;

                case Layer.Pants:

                    robe = mobile.FindItemByLayer(Layer.Robe);
                    pants = mobile.FindItemByLayer(Layer.Pants);

                    //Eventine ignores pants layers
                    if ((Settings.GlobalSettings.CustomServer != Settings.CustomServers.Eventine && mobile.FindItemByLayer(Layer.Legs) != null)
                        || robe != null && robe.Graphic == 0x0504
                    )
                    {
                        return true;
                    }

                    if (
                        pants != null
                        && (
                            pants.Graphic == 0x01EB
                            || pants.Graphic == 0x03E5
                            || pants.Graphic == 0x03eB
                        )
                    )
                    {
                        Item skirt = mobile.FindItemByLayer(Layer.Skirt);

                        if (skirt != null && skirt.Graphic != 0x01C7 && skirt.Graphic != 0x01E4)
                        {
                            return true;
                        }

                        if (
                            robe != null
                            && robe.Graphic != 0x0229
                            && (robe.Graphic <= 0x04E7 || robe.Graphic > 0x04EB)
                        )
                        {
                            return true;
                        }
                    }

                    break;

                case Layer.Tunic:
                    robe = mobile.FindItemByLayer(Layer.Robe);
                    Item tunic = mobile.FindItemByLayer(Layer.Tunic);

                    if (tunic != null && tunic.Graphic == 0x0238)
                    {
                        return robe != null
                            && robe.Graphic != 0x9985
                            && robe.Graphic != 0x9986
                            && robe.Graphic != 0xA412
                            && robe.Graphic != 0xA2CB
                            && robe.Graphic != 0xA2CA;
                    }

                    break;

                case Layer.Torso:
                    robe = mobile.FindItemByLayer(Layer.Robe);

                    if (
                        robe != null
                        && robe.Graphic != 0
                        && robe.Graphic != 0x9985
                        && robe.Graphic != 0x9986
                        && robe.Graphic != 0xA412
                        && robe.Graphic != 0xA2CB
                        && robe.Graphic != 0xA2CA
                    )
                    {
                        return true;
                    }
                    else
                    {
                        tunic = mobile.FindItemByLayer(Layer.Tunic);

                        if (tunic != null && tunic.Graphic != 0x1541 && tunic.Graphic != 0x1542)
                        {
                            Item torso = mobile.FindItemByLayer(Layer.Torso);

                            if (
                                torso != null
                                && (torso.Graphic == 0x782A || torso.Graphic == 0x782B)
                            )
                            {
                                return true;
                            }
                        }
                    }

                    break;

                case Layer.Arms:
                    robe = mobile.FindItemByLayer(Layer.Robe);

                    return robe != null
                        && robe.Graphic != 0
                        && robe.Graphic != 0x9985
                        && robe.Graphic != 0x9986
                        && robe.Graphic != 0xA412
                        && robe.Graphic != 0xA2CB
                        && robe.Graphic != 0xA2CA;

                case Layer.Helmet:
                case Layer.Hair:
                    robe = mobile.FindItemByLayer(Layer.Robe);

                    if (robe != null)
                    {
                        if (robe.Graphic > 0x3173)
                        {
                            if (robe.Graphic == 0x4B9D || robe.Graphic == 0x7816)
                            {
                                return true;
                            }
                        }
                        else
                        {
                            if (robe.Graphic <= 0x2687)
                            {
                                if (robe.Graphic < 0x2683)
                                {
                                    return robe.Graphic >= 0x204E && robe.Graphic <= 0x204F;
                                }

                                return true;
                            }

                            if (robe.Graphic == 0x2FB9 || robe.Graphic == 0x3173)
                            {
                                return true;
                            }
                        }
                    }

                    break;
            }

            return false;
        }
    }
}
