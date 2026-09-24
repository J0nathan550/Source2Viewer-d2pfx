using GUI.Controls;

namespace GUI.Forms
{
    partial class CharacterSelectForm
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }

            if (disposing)
            {
                previewViewer?.Dispose();
                previewViewer = null;
            }

            base.Dispose(disposing);

            // The group labels are laid out with it until the controls are disposed
            if (disposing)
            {
                groupFont?.Dispose();
                groupFont = null;
            }
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            components = new System.ComponentModel.Container();
            mainSplitContainer = new System.Windows.Forms.SplitContainer();
            previewPanel = new System.Windows.Forms.Panel();
            previewStatusLabel = new System.Windows.Forms.Label();
            controlsTable = new System.Windows.Forms.TableLayoutPanel();
            heroNavigationTable = new System.Windows.Forms.TableLayoutPanel();
            previousHeroButton = new ThemedButton();
            heroNameLabel = new System.Windows.Forms.Label();
            nextHeroButton = new ThemedButton();
            heroSearchTextBox = new ThemedTextBox();
            itemSetLabel = new System.Windows.Forms.Label();
            itemSetComboBox = new SearchableComboBox();
            loadoutTabControl = new ThemedTabControl();
            itemsTabPage = new ThemedTabPage();
            slotsPanel = new System.Windows.Forms.Panel();
            slotsTable = new System.Windows.Forms.TableLayoutPanel();
            iconsTabPage = new ThemedTabPage();
            iconsPanel = new System.Windows.Forms.Panel();
            iconsTable = new System.Windows.Forms.TableLayoutPanel();
            soundsTabPage = new ThemedTabPage();
            soundsPanel = new System.Windows.Forms.Panel();
            soundsTable = new System.Windows.Forms.TableLayoutPanel();
            effectsTabPage = new ThemedTabPage();
            effectsPanel = new System.Windows.Forms.Panel();
            effectsTable = new System.Windows.Forms.TableLayoutPanel();
            previewEffectsCheckBox = new System.Windows.Forms.CheckBox();
            includeGroupBox = new ThemedGroupBox();
            includeTable = new System.Windows.Forms.TableLayoutPanel();
            heroModelCheckBox = new System.Windows.Forms.CheckBox();
            itemModelsCheckBox = new System.Windows.Forms.CheckBox();
            itemParticlesCheckBox = new System.Windows.Forms.CheckBox();
            heroParticlesCheckBox = new System.Windows.Forms.CheckBox();
            itemSoundsCheckBox = new System.Windows.Forms.CheckBox();
            heroSoundsCheckBox = new System.Windows.Forms.CheckBox();
            heroVoiceCheckBox = new System.Windows.Forms.CheckBox();
            iconsCheckBox = new System.Windows.Forms.CheckBox();
            soundsCheckBox = new System.Windows.Forms.CheckBox();
            includeAudioCheckBox = new System.Windows.Forms.CheckBox();
            pedestalCheckBox = new System.Windows.Forms.CheckBox();
            replaceDefaultsCheckBox = new System.Windows.Forms.CheckBox();
            replaceSharedParticlesCheckBox = new System.Windows.Forms.CheckBox();
            outputGroupBox = new ThemedGroupBox();
            outputTable = new System.Windows.Forms.TableLayoutPanel();
            contentFolderLabel = new System.Windows.Forms.Label();
            contentFolderTextBox = new ThemedTextBox();
            contentFolderButton = new ThemedButton();
            gameFolderLabel = new System.Windows.Forms.Label();
            gameFolderTextBox = new ThemedTextBox();
            gameFolderButton = new ThemedButton();
            buttonsTable = new System.Windows.Forms.TableLayoutPanel();
            summaryLabel = new System.Windows.Forms.Label();
            cancelButton = new ThemedButton();
            exportButton = new ThemedButton();
            previewTimer = new System.Windows.Forms.Timer(components);
            toolTip = new System.Windows.Forms.ToolTip(components);
            ((System.ComponentModel.ISupportInitialize)mainSplitContainer).BeginInit();
            mainSplitContainer.Panel1.SuspendLayout();
            mainSplitContainer.Panel2.SuspendLayout();
            mainSplitContainer.SuspendLayout();
            previewPanel.SuspendLayout();
            controlsTable.SuspendLayout();
            heroNavigationTable.SuspendLayout();
            loadoutTabControl.SuspendLayout();
            itemsTabPage.SuspendLayout();
            slotsPanel.SuspendLayout();
            iconsTabPage.SuspendLayout();
            iconsPanel.SuspendLayout();
            soundsTabPage.SuspendLayout();
            soundsPanel.SuspendLayout();
            effectsTabPage.SuspendLayout();
            effectsPanel.SuspendLayout();
            includeGroupBox.SuspendLayout();
            includeTable.SuspendLayout();
            outputGroupBox.SuspendLayout();
            outputTable.SuspendLayout();
            buttonsTable.SuspendLayout();
            SuspendLayout();
            //
            // mainSplitContainer
            //
            mainSplitContainer.Dock = System.Windows.Forms.DockStyle.Fill;
            mainSplitContainer.FixedPanel = System.Windows.Forms.FixedPanel.Panel2;
            mainSplitContainer.Location = new System.Drawing.Point(8, 8);
            mainSplitContainer.Name = "mainSplitContainer";
            //
            // mainSplitContainer.Panel1
            //
            mainSplitContainer.Panel1.Controls.Add(previewPanel);
            mainSplitContainer.Panel1MinSize = 200;
            //
            // mainSplitContainer.Panel2
            //
            mainSplitContainer.Panel2.Controls.Add(controlsTable);
            mainSplitContainer.Panel2MinSize = 440;
            mainSplitContainer.Size = new System.Drawing.Size(1208, 824);
            mainSplitContainer.SplitterDistance = 660;
            mainSplitContainer.SplitterWidth = 8;
            mainSplitContainer.TabIndex = 0;
            mainSplitContainer.TabStop = false;
            mainSplitContainer.SplitterMoved += MainSplitContainer_SplitterMoved;
            mainSplitContainer.Paint += MainSplitContainer_Paint;
            //
            // previewPanel
            //
            previewPanel.Controls.Add(previewStatusLabel);
            previewPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            previewPanel.Location = new System.Drawing.Point(0, 0);
            previewPanel.Name = "previewPanel";
            previewPanel.Size = new System.Drawing.Size(660, 824);
            previewPanel.TabIndex = 0;
            //
            // previewStatusLabel
            //
            previewStatusLabel.Dock = System.Windows.Forms.DockStyle.Fill;
            previewStatusLabel.Location = new System.Drawing.Point(0, 0);
            previewStatusLabel.Name = "previewStatusLabel";
            previewStatusLabel.Size = new System.Drawing.Size(660, 824);
            previewStatusLabel.TabIndex = 0;
            previewStatusLabel.Text = "Loading preview...";
            previewStatusLabel.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            //
            // controlsTable
            //
            controlsTable.ColumnCount = 1;
            controlsTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            controlsTable.Controls.Add(heroNavigationTable, 0, 0);
            controlsTable.Controls.Add(heroSearchTextBox, 0, 1);
            controlsTable.Controls.Add(itemSetLabel, 0, 2);
            controlsTable.Controls.Add(itemSetComboBox, 0, 3);
            controlsTable.Controls.Add(loadoutTabControl, 0, 4);
            controlsTable.Controls.Add(includeGroupBox, 0, 5);
            controlsTable.Controls.Add(outputGroupBox, 0, 6);
            controlsTable.Controls.Add(buttonsTable, 0, 7);
            controlsTable.Dock = System.Windows.Forms.DockStyle.Fill;
            controlsTable.Location = new System.Drawing.Point(0, 0);
            controlsTable.Name = "controlsTable";
            controlsTable.RowCount = 8;
            controlsTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            controlsTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            controlsTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            controlsTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            controlsTable.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            controlsTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            controlsTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            controlsTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            controlsTable.Size = new System.Drawing.Size(540, 824);
            controlsTable.TabIndex = 1;
            //
            // heroNavigationTable
            //
            heroNavigationTable.AutoSize = true;
            heroNavigationTable.ColumnCount = 3;
            heroNavigationTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 48F));
            heroNavigationTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            heroNavigationTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 48F));
            heroNavigationTable.Controls.Add(previousHeroButton, 0, 0);
            heroNavigationTable.Controls.Add(heroNameLabel, 1, 0);
            heroNavigationTable.Controls.Add(nextHeroButton, 2, 0);
            heroNavigationTable.Dock = System.Windows.Forms.DockStyle.Fill;
            heroNavigationTable.Location = new System.Drawing.Point(3, 3);
            heroNavigationTable.Name = "heroNavigationTable";
            heroNavigationTable.RowCount = 1;
            heroNavigationTable.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 48F));
            heroNavigationTable.Size = new System.Drawing.Size(428, 48);
            heroNavigationTable.TabIndex = 0;
            //
            // previousHeroButton
            //
            previousHeroButton.Dock = System.Windows.Forms.DockStyle.Fill;
            previousHeroButton.Font = new System.Drawing.Font("Segoe UI", 14F, System.Drawing.FontStyle.Bold);
            previousHeroButton.Location = new System.Drawing.Point(3, 3);
            previousHeroButton.Name = "previousHeroButton";
            previousHeroButton.Size = new System.Drawing.Size(42, 42);
            previousHeroButton.TabIndex = 0;
            previousHeroButton.Text = "<";
            previousHeroButton.UseVisualStyleBackColor = false;
            previousHeroButton.Click += PreviousHeroButton_Click;
            //
            // heroNameLabel
            //
            heroNameLabel.AutoEllipsis = true;
            heroNameLabel.Dock = System.Windows.Forms.DockStyle.Fill;
            heroNameLabel.Font = new System.Drawing.Font("Segoe UI", 16F, System.Drawing.FontStyle.Bold);
            heroNameLabel.Location = new System.Drawing.Point(51, 0);
            heroNameLabel.Name = "heroNameLabel";
            heroNameLabel.Size = new System.Drawing.Size(326, 48);
            heroNameLabel.TabIndex = 1;
            heroNameLabel.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            //
            // nextHeroButton
            //
            nextHeroButton.Dock = System.Windows.Forms.DockStyle.Fill;
            nextHeroButton.Font = new System.Drawing.Font("Segoe UI", 14F, System.Drawing.FontStyle.Bold);
            nextHeroButton.Location = new System.Drawing.Point(383, 3);
            nextHeroButton.Name = "nextHeroButton";
            nextHeroButton.Size = new System.Drawing.Size(42, 42);
            nextHeroButton.TabIndex = 2;
            nextHeroButton.Text = ">";
            nextHeroButton.UseVisualStyleBackColor = false;
            nextHeroButton.Click += NextHeroButton_Click;
            //
            // heroSearchTextBox
            //
            heroSearchTextBox.Dock = System.Windows.Forms.DockStyle.Fill;
            heroSearchTextBox.Location = new System.Drawing.Point(3, 57);
            heroSearchTextBox.Name = "heroSearchTextBox";
            heroSearchTextBox.PlaceholderText = "Search heroes";
            heroSearchTextBox.Size = new System.Drawing.Size(428, 25);
            heroSearchTextBox.TabIndex = 1;
            heroSearchTextBox.TextChanged += HeroSearchTextBox_TextChanged;
            //
            // itemSetLabel
            //
            itemSetLabel.AutoSize = true;
            itemSetLabel.Location = new System.Drawing.Point(3, 91);
            itemSetLabel.Margin = new System.Windows.Forms.Padding(3, 6, 3, 0);
            itemSetLabel.Name = "itemSetLabel";
            itemSetLabel.Size = new System.Drawing.Size(58, 19);
            itemSetLabel.TabIndex = 2;
            itemSetLabel.Text = "Item set";
            //
            // itemSetComboBox
            //
            itemSetComboBox.Dock = System.Windows.Forms.DockStyle.Fill;
            itemSetComboBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            itemSetComboBox.Location = new System.Drawing.Point(3, 113);
            itemSetComboBox.MaxDropDownItems = 20;
            itemSetComboBox.Name = "itemSetComboBox";
            itemSetComboBox.Size = new System.Drawing.Size(428, 26);
            itemSetComboBox.TabIndex = 3;
            itemSetComboBox.SelectedIndexChanged += ItemSetComboBox_SelectedIndexChanged;
            //
            // loadoutTabControl
            //
            loadoutTabControl.BaseTabWidth = 120;
            loadoutTabControl.Controls.Add(itemsTabPage);
            loadoutTabControl.Controls.Add(iconsTabPage);
            loadoutTabControl.Controls.Add(soundsTabPage);
            loadoutTabControl.Controls.Add(effectsTabPage);
            loadoutTabControl.Dock = System.Windows.Forms.DockStyle.Fill;
            loadoutTabControl.DrawMode = System.Windows.Forms.TabDrawMode.OwnerDrawFixed;
            loadoutTabControl.Location = new System.Drawing.Point(3, 145);
            loadoutTabControl.Name = "loadoutTabControl";
            loadoutTabControl.Padding = new System.Drawing.Point(12, 8);
            loadoutTabControl.SelectedIndex = 0;
            loadoutTabControl.SelectionLine = true;
            loadoutTabControl.Size = new System.Drawing.Size(428, 330);
            loadoutTabControl.TabHeight = 32;
            loadoutTabControl.TabIndex = 4;
            loadoutTabControl.TabTopRadius = 0;
            //
            // itemsTabPage
            //
            itemsTabPage.Controls.Add(slotsPanel);
            itemsTabPage.Location = new System.Drawing.Point(4, 36);
            itemsTabPage.Name = "itemsTabPage";
            itemsTabPage.Size = new System.Drawing.Size(460, 290);
            itemsTabPage.TabIndex = 0;
            itemsTabPage.Text = "Items";
            //
            // slotsPanel
            //
            slotsPanel.AutoScroll = true;
            slotsPanel.Controls.Add(slotsTable);
            slotsPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            slotsPanel.Location = new System.Drawing.Point(0, 0);
            slotsPanel.Name = "slotsPanel";
            slotsPanel.Size = new System.Drawing.Size(460, 290);
            slotsPanel.TabIndex = 0;
            //
            // slotsTable
            //
            slotsTable.AutoSize = true;
            slotsTable.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            slotsTable.ColumnCount = 4;
            slotsTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            slotsTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            slotsTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            slotsTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            slotsTable.Dock = System.Windows.Forms.DockStyle.Top;
            slotsTable.Location = new System.Drawing.Point(0, 0);
            slotsTable.Name = "slotsTable";
            slotsTable.RowCount = 0;
            slotsTable.Size = new System.Drawing.Size(460, 0);
            slotsTable.TabIndex = 0;
            //
            // iconsTabPage
            //
            iconsTabPage.Controls.Add(iconsPanel);
            iconsTabPage.Location = new System.Drawing.Point(4, 36);
            iconsTabPage.Name = "iconsTabPage";
            iconsTabPage.Size = new System.Drawing.Size(460, 290);
            iconsTabPage.TabIndex = 1;
            iconsTabPage.Text = "Icons";
            //
            // iconsPanel
            //
            iconsPanel.AutoScroll = true;
            iconsPanel.Controls.Add(iconsTable);
            iconsPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            iconsPanel.Location = new System.Drawing.Point(0, 0);
            iconsPanel.Name = "iconsPanel";
            iconsPanel.Size = new System.Drawing.Size(460, 290);
            iconsPanel.TabIndex = 0;
            //
            // iconsTable
            //
            iconsTable.AutoSize = true;
            iconsTable.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            iconsTable.ColumnCount = 3;
            iconsTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            iconsTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            iconsTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            iconsTable.Dock = System.Windows.Forms.DockStyle.Top;
            iconsTable.Location = new System.Drawing.Point(0, 0);
            iconsTable.Name = "iconsTable";
            iconsTable.RowCount = 0;
            iconsTable.Size = new System.Drawing.Size(460, 0);
            iconsTable.TabIndex = 0;
            //
            // soundsTabPage
            //
            soundsTabPage.Controls.Add(soundsPanel);
            soundsTabPage.Location = new System.Drawing.Point(4, 36);
            soundsTabPage.Name = "soundsTabPage";
            soundsTabPage.Size = new System.Drawing.Size(460, 290);
            soundsTabPage.TabIndex = 2;
            soundsTabPage.Text = "Sounds";
            //
            // soundsPanel
            //
            soundsPanel.AutoScroll = true;
            soundsPanel.Controls.Add(soundsTable);
            soundsPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            soundsPanel.Location = new System.Drawing.Point(0, 0);
            soundsPanel.Name = "soundsPanel";
            soundsPanel.Size = new System.Drawing.Size(460, 290);
            soundsPanel.TabIndex = 0;
            //
            // soundsTable
            //
            soundsTable.AutoSize = true;
            soundsTable.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            soundsTable.ColumnCount = 2;
            soundsTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            soundsTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            soundsTable.Dock = System.Windows.Forms.DockStyle.Top;
            soundsTable.Location = new System.Drawing.Point(0, 0);
            soundsTable.Name = "soundsTable";
            soundsTable.RowCount = 0;
            soundsTable.Size = new System.Drawing.Size(460, 0);
            soundsTable.TabIndex = 0;
            //
            // effectsTabPage
            //
            effectsTabPage.Controls.Add(effectsPanel);
            effectsTabPage.Location = new System.Drawing.Point(4, 36);
            effectsTabPage.Name = "effectsTabPage";
            effectsTabPage.Size = new System.Drawing.Size(460, 290);
            effectsTabPage.TabIndex = 3;
            effectsTabPage.Text = "Effects";
            //
            // effectsPanel
            //
            effectsPanel.AutoScroll = true;
            effectsPanel.Controls.Add(effectsTable);
            effectsPanel.Controls.Add(previewEffectsCheckBox);
            effectsPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            effectsPanel.Location = new System.Drawing.Point(0, 0);
            effectsPanel.Name = "effectsPanel";
            effectsPanel.Size = new System.Drawing.Size(460, 290);
            effectsPanel.TabIndex = 0;
            //
            // effectsTable
            //
            effectsTable.AutoSize = true;
            effectsTable.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            effectsTable.ColumnCount = 1;
            effectsTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            effectsTable.Dock = System.Windows.Forms.DockStyle.Top;
            effectsTable.Location = new System.Drawing.Point(0, 0);
            effectsTable.Name = "effectsTable";
            effectsTable.RowCount = 0;
            effectsTable.Size = new System.Drawing.Size(460, 0);
            effectsTable.TabIndex = 1;
            //
            // previewEffectsCheckBox
            //
            previewEffectsCheckBox.AutoSize = true;
            previewEffectsCheckBox.Checked = true;
            previewEffectsCheckBox.CheckState = System.Windows.Forms.CheckState.Checked;
            previewEffectsCheckBox.Dock = System.Windows.Forms.DockStyle.Top;
            previewEffectsCheckBox.Name = "previewEffectsCheckBox";
            previewEffectsCheckBox.Padding = new System.Windows.Forms.Padding(6, 6, 0, 0);
            previewEffectsCheckBox.TabIndex = 0;
            previewEffectsCheckBox.Text = "Show effects in the preview";
            previewEffectsCheckBox.UseVisualStyleBackColor = true;
            previewEffectsCheckBox.CheckedChanged += PreviewEffectsCheckBox_CheckedChanged;
            //
            // includeGroupBox
            //
            includeGroupBox.AutoSize = true;
            includeGroupBox.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            includeGroupBox.Controls.Add(includeTable);
            includeGroupBox.Dock = System.Windows.Forms.DockStyle.Fill;
            includeGroupBox.Location = new System.Drawing.Point(3, 550);
            includeGroupBox.Name = "includeGroupBox";
            includeGroupBox.Size = new System.Drawing.Size(428, 157);
            includeGroupBox.TabIndex = 5;
            includeGroupBox.TabStop = false;
            includeGroupBox.Text = "Export";
            //
            // includeTable
            //
            includeTable.AutoSize = true;
            includeTable.ColumnCount = 3;
            includeTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 33.33F));
            includeTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 33.33F));
            includeTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 33.34F));
            includeTable.Controls.Add(heroModelCheckBox, 0, 0);
            includeTable.Controls.Add(itemModelsCheckBox, 1, 0);
            includeTable.Controls.Add(pedestalCheckBox, 2, 0);
            includeTable.Controls.Add(itemParticlesCheckBox, 0, 1);
            includeTable.Controls.Add(heroParticlesCheckBox, 1, 1);
            includeTable.Controls.Add(iconsCheckBox, 2, 1);
            includeTable.Controls.Add(itemSoundsCheckBox, 0, 2);
            includeTable.Controls.Add(heroSoundsCheckBox, 1, 2);
            includeTable.Controls.Add(heroVoiceCheckBox, 2, 2);
            includeTable.Controls.Add(includeAudioCheckBox, 0, 3);
            includeTable.SetColumnSpan(includeAudioCheckBox, 2);
            includeTable.Controls.Add(soundsCheckBox, 2, 3);
            includeTable.Controls.Add(replaceDefaultsCheckBox, 0, 4);
            includeTable.SetColumnSpan(replaceDefaultsCheckBox, 2);
            includeTable.Controls.Add(replaceSharedParticlesCheckBox, 2, 4);
            includeTable.Dock = System.Windows.Forms.DockStyle.Top;
            includeTable.Location = new System.Drawing.Point(3, 21);
            includeTable.Name = "includeTable";
            includeTable.RowCount = 5;
            includeTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            includeTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            includeTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            includeTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            includeTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            includeTable.Size = new System.Drawing.Size(422, 116);
            includeTable.TabIndex = 0;
            //
            // heroModelCheckBox
            //
            heroModelCheckBox.AutoSize = true;
            heroModelCheckBox.Checked = true;
            heroModelCheckBox.CheckState = System.Windows.Forms.CheckState.Checked;
            heroModelCheckBox.Name = "heroModelCheckBox";
            heroModelCheckBox.TabIndex = 0;
            heroModelCheckBox.Text = "Hero base model";
            heroModelCheckBox.UseVisualStyleBackColor = true;
            //
            // itemModelsCheckBox
            //
            itemModelsCheckBox.AutoSize = true;
            itemModelsCheckBox.Checked = true;
            itemModelsCheckBox.CheckState = System.Windows.Forms.CheckState.Checked;
            itemModelsCheckBox.Name = "itemModelsCheckBox";
            itemModelsCheckBox.TabIndex = 1;
            itemModelsCheckBox.Text = "Item models";
            itemModelsCheckBox.UseVisualStyleBackColor = true;
            //
            // itemParticlesCheckBox
            //
            itemParticlesCheckBox.AutoSize = true;
            itemParticlesCheckBox.Checked = true;
            itemParticlesCheckBox.CheckState = System.Windows.Forms.CheckState.Checked;
            itemParticlesCheckBox.Name = "itemParticlesCheckBox";
            itemParticlesCheckBox.TabIndex = 2;
            itemParticlesCheckBox.Text = "Item particles";
            itemParticlesCheckBox.UseVisualStyleBackColor = true;
            //
            // heroParticlesCheckBox
            //
            heroParticlesCheckBox.AutoSize = true;
            heroParticlesCheckBox.Name = "heroParticlesCheckBox";
            heroParticlesCheckBox.TabIndex = 3;
            heroParticlesCheckBox.Text = "All hero particles";
            heroParticlesCheckBox.UseVisualStyleBackColor = true;
            //
            // itemSoundsCheckBox
            //
            itemSoundsCheckBox.AutoSize = true;
            itemSoundsCheckBox.Checked = true;
            itemSoundsCheckBox.CheckState = System.Windows.Forms.CheckState.Checked;
            itemSoundsCheckBox.Name = "itemSoundsCheckBox";
            itemSoundsCheckBox.TabIndex = 4;
            itemSoundsCheckBox.Text = "Item sound events";
            itemSoundsCheckBox.UseVisualStyleBackColor = true;
            //
            // heroSoundsCheckBox
            //
            heroSoundsCheckBox.AutoSize = true;
            heroSoundsCheckBox.Checked = true;
            heroSoundsCheckBox.CheckState = System.Windows.Forms.CheckState.Checked;
            heroSoundsCheckBox.Name = "heroSoundsCheckBox";
            heroSoundsCheckBox.TabIndex = 5;
            heroSoundsCheckBox.Text = "Hero sound events";
            heroSoundsCheckBox.UseVisualStyleBackColor = true;
            //
            // heroVoiceCheckBox
            //
            heroVoiceCheckBox.AutoSize = true;
            heroVoiceCheckBox.Checked = true;
            heroVoiceCheckBox.CheckState = System.Windows.Forms.CheckState.Checked;
            heroVoiceCheckBox.Name = "heroVoiceCheckBox";
            heroVoiceCheckBox.TabIndex = 6;
            heroVoiceCheckBox.Text = "Voice line events";
            heroVoiceCheckBox.UseVisualStyleBackColor = true;
            //
            // iconsCheckBox
            //
            iconsCheckBox.AutoSize = true;
            iconsCheckBox.Checked = true;
            iconsCheckBox.CheckState = System.Windows.Forms.CheckState.Checked;
            iconsCheckBox.Name = "iconsCheckBox";
            iconsCheckBox.TabIndex = 7;
            iconsCheckBox.Text = "Replace icons";
            iconsCheckBox.UseVisualStyleBackColor = true;
            //
            // soundsCheckBox
            //
            soundsCheckBox.AutoSize = true;
            soundsCheckBox.Checked = true;
            soundsCheckBox.CheckState = System.Windows.Forms.CheckState.Checked;
            soundsCheckBox.Name = "soundsCheckBox";
            soundsCheckBox.TabIndex = 12;
            soundsCheckBox.Text = "Replace sounds";
            soundsCheckBox.UseVisualStyleBackColor = true;
            //
            // includeAudioCheckBox
            //
            includeAudioCheckBox.AutoSize = true;
            includeAudioCheckBox.Name = "includeAudioCheckBox";
            includeAudioCheckBox.TabIndex = 8;
            includeAudioCheckBox.Text = "Include the sounds they play (.mp3, .wav)";
            includeAudioCheckBox.UseVisualStyleBackColor = true;
            //
            // pedestalCheckBox
            //
            pedestalCheckBox.AutoSize = true;
            pedestalCheckBox.Name = "pedestalCheckBox";
            pedestalCheckBox.TabIndex = 11;
            pedestalCheckBox.Text = "Pedestal";
            pedestalCheckBox.UseVisualStyleBackColor = true;
            //
            // replaceDefaultsCheckBox
            //
            replaceDefaultsCheckBox.AutoSize = true;
            replaceDefaultsCheckBox.Checked = true;
            replaceDefaultsCheckBox.CheckState = System.Windows.Forms.CheckState.Checked;
            replaceDefaultsCheckBox.Name = "replaceDefaultsCheckBox";
            replaceDefaultsCheckBox.TabIndex = 9;
            replaceDefaultsCheckBox.Text = "Replace default assets";
            replaceDefaultsCheckBox.UseVisualStyleBackColor = true;
            replaceDefaultsCheckBox.CheckedChanged += ReplaceDefaultsCheckBox_CheckedChanged;
            //
            // replaceSharedParticlesCheckBox
            //
            replaceSharedParticlesCheckBox.AutoSize = true;
            replaceSharedParticlesCheckBox.Name = "replaceSharedParticlesCheckBox";
            replaceSharedParticlesCheckBox.TabIndex = 10;
            replaceSharedParticlesCheckBox.Text = "Also shared particles";
            replaceSharedParticlesCheckBox.UseVisualStyleBackColor = true;
            //
            // outputGroupBox
            //
            outputGroupBox.AutoSize = true;
            outputGroupBox.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            outputGroupBox.Controls.Add(outputTable);
            outputGroupBox.Dock = System.Windows.Forms.DockStyle.Fill;
            outputGroupBox.Name = "outputGroupBox";
            outputGroupBox.TabIndex = 6;
            outputGroupBox.TabStop = false;
            outputGroupBox.Text = "Addon folders";
            //
            // outputTable
            //
            outputTable.AutoSize = true;
            outputTable.ColumnCount = 3;
            outputTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            outputTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            outputTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            outputTable.Controls.Add(contentFolderLabel, 0, 0);
            outputTable.Controls.Add(contentFolderTextBox, 1, 0);
            outputTable.Controls.Add(contentFolderButton, 2, 0);
            outputTable.Controls.Add(gameFolderLabel, 0, 1);
            outputTable.Controls.Add(gameFolderTextBox, 1, 1);
            outputTable.Controls.Add(gameFolderButton, 2, 1);
            outputTable.Dock = System.Windows.Forms.DockStyle.Top;
            outputTable.Name = "outputTable";
            outputTable.RowCount = 2;
            outputTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            outputTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            outputTable.TabIndex = 0;
            //
            // contentFolderLabel
            //
            contentFolderLabel.Anchor = System.Windows.Forms.AnchorStyles.Left;
            contentFolderLabel.AutoSize = true;
            contentFolderLabel.Name = "contentFolderLabel";
            contentFolderLabel.TabIndex = 0;
            contentFolderLabel.Text = "Content";
            //
            // contentFolderTextBox
            //
            contentFolderTextBox.Anchor = System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            contentFolderTextBox.Name = "contentFolderTextBox";
            contentFolderTextBox.PlaceholderText = "dota 2 beta\\content\\dota_addons\\<addon>";
            contentFolderTextBox.TabIndex = 1;
            contentFolderTextBox.WordWrap = false;
            contentFolderTextBox.TextChanged += ContentFolderTextBox_TextChanged;
            //
            // contentFolderButton
            //
            contentFolderButton.Name = "contentFolderButton";
            contentFolderButton.Size = new System.Drawing.Size(36, 27);
            contentFolderButton.TabIndex = 2;
            contentFolderButton.Text = "...";
            contentFolderButton.UseVisualStyleBackColor = false;
            contentFolderButton.Click += ContentFolderButton_Click;
            //
            // gameFolderLabel
            //
            gameFolderLabel.Anchor = System.Windows.Forms.AnchorStyles.Left;
            gameFolderLabel.AutoSize = true;
            gameFolderLabel.Name = "gameFolderLabel";
            gameFolderLabel.TabIndex = 3;
            gameFolderLabel.Text = "Game";
            //
            // gameFolderTextBox
            //
            gameFolderTextBox.Anchor = System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            gameFolderTextBox.Name = "gameFolderTextBox";
            gameFolderTextBox.PlaceholderText = "dota 2 beta\\game\\dota_addons\\<addon>";
            gameFolderTextBox.TabIndex = 4;
            gameFolderTextBox.TextChanged += GameFolderTextBox_TextChanged;
            gameFolderTextBox.WordWrap = false;
            //
            // gameFolderButton
            //
            gameFolderButton.Name = "gameFolderButton";
            gameFolderButton.Size = new System.Drawing.Size(36, 27);
            gameFolderButton.TabIndex = 5;
            gameFolderButton.Text = "...";
            gameFolderButton.UseVisualStyleBackColor = false;
            gameFolderButton.Click += GameFolderButton_Click;
            //
            // buttonsTable
            //
            buttonsTable.AutoSize = true;
            buttonsTable.ColumnCount = 3;
            buttonsTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            buttonsTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            buttonsTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            buttonsTable.Controls.Add(summaryLabel, 0, 0);
            buttonsTable.Controls.Add(exportButton, 1, 0);
            buttonsTable.Controls.Add(cancelButton, 2, 0);
            buttonsTable.Dock = System.Windows.Forms.DockStyle.Fill;
            buttonsTable.Location = new System.Drawing.Point(3, 713);
            buttonsTable.Name = "buttonsTable";
            buttonsTable.RowCount = 1;
            buttonsTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            buttonsTable.Size = new System.Drawing.Size(428, 43);
            buttonsTable.TabIndex = 7;
            //
            // summaryLabel
            //
            summaryLabel.AutoEllipsis = true;
            summaryLabel.Dock = System.Windows.Forms.DockStyle.Fill;
            summaryLabel.Name = "summaryLabel";
            summaryLabel.TabIndex = 0;
            summaryLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            //
            // cancelButton
            //
            cancelButton.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            cancelButton.Name = "cancelButton";
            cancelButton.Size = new System.Drawing.Size(96, 37);
            cancelButton.TabIndex = 2;
            cancelButton.Text = "Cancel";
            cancelButton.UseVisualStyleBackColor = false;
            //
            // exportButton
            //
            exportButton.Name = "exportButton";
            exportButton.Size = new System.Drawing.Size(96, 37);
            exportButton.TabIndex = 1;
            exportButton.Text = "Export...";
            exportButton.UseVisualStyleBackColor = false;
            exportButton.Click += ExportButton_Click;
            //
            // previewTimer
            //
            previewTimer.Interval = 250;
            previewTimer.Tick += PreviewTimer_Tick;
            //
            // CharacterSelectForm
            //
            AcceptButton = exportButton;
            AutoScaleDimensions = new System.Drawing.SizeF(7F, 17F);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            CancelButton = cancelButton;
            ClientSize = new System.Drawing.Size(1224, 840);
            Controls.Add(mainSplitContainer);
            Font = new System.Drawing.Font("Segoe UI", 10F);
            Padding = new System.Windows.Forms.Padding(8);
            MinimumSize = new System.Drawing.Size(900, 700);
            Name = "CharacterSelectForm";
            ShowIcon = false;
            ShowInTaskbar = false;
            StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            Text = "Choose Character";
            mainSplitContainer.Panel1.ResumeLayout(false);
            mainSplitContainer.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)mainSplitContainer).EndInit();
            mainSplitContainer.ResumeLayout(false);
            previewPanel.ResumeLayout(false);
            controlsTable.ResumeLayout(false);
            controlsTable.PerformLayout();
            heroNavigationTable.ResumeLayout(false);
            loadoutTabControl.ResumeLayout(false);
            itemsTabPage.ResumeLayout(false);
            slotsPanel.ResumeLayout(false);
            slotsPanel.PerformLayout();
            iconsTabPage.ResumeLayout(false);
            iconsPanel.ResumeLayout(false);
            iconsPanel.PerformLayout();
            soundsTabPage.ResumeLayout(false);
            soundsPanel.ResumeLayout(false);
            soundsPanel.PerformLayout();
            effectsTabPage.ResumeLayout(false);
            effectsPanel.ResumeLayout(false);
            effectsPanel.PerformLayout();
            includeGroupBox.ResumeLayout(false);
            includeGroupBox.PerformLayout();
            includeTable.ResumeLayout(false);
            includeTable.PerformLayout();
            outputGroupBox.ResumeLayout(false);
            outputGroupBox.PerformLayout();
            outputTable.ResumeLayout(false);
            outputTable.PerformLayout();
            buttonsTable.ResumeLayout(false);
            ResumeLayout(false);
        }

        #endregion

        private System.Windows.Forms.SplitContainer mainSplitContainer;
        private System.Windows.Forms.Panel previewPanel;
        private System.Windows.Forms.Label previewStatusLabel;
        private System.Windows.Forms.TableLayoutPanel controlsTable;
        private System.Windows.Forms.TableLayoutPanel heroNavigationTable;
        private ThemedButton previousHeroButton;
        private System.Windows.Forms.Label heroNameLabel;
        private ThemedButton nextHeroButton;
        private ThemedTextBox heroSearchTextBox;
        private System.Windows.Forms.Label itemSetLabel;
        private SearchableComboBox itemSetComboBox;
        private ThemedTabControl loadoutTabControl;
        private ThemedTabPage itemsTabPage;
        private System.Windows.Forms.Panel slotsPanel;
        private System.Windows.Forms.TableLayoutPanel slotsTable;
        private ThemedTabPage iconsTabPage;
        private System.Windows.Forms.Panel iconsPanel;
        private System.Windows.Forms.TableLayoutPanel iconsTable;
        private ThemedTabPage soundsTabPage;
        private System.Windows.Forms.Panel soundsPanel;
        private System.Windows.Forms.TableLayoutPanel soundsTable;
        private ThemedTabPage effectsTabPage;
        private System.Windows.Forms.Panel effectsPanel;
        private System.Windows.Forms.TableLayoutPanel effectsTable;
        private System.Windows.Forms.CheckBox previewEffectsCheckBox;
        private ThemedGroupBox includeGroupBox;
        private System.Windows.Forms.TableLayoutPanel includeTable;
        private System.Windows.Forms.CheckBox heroModelCheckBox;
        private System.Windows.Forms.CheckBox itemModelsCheckBox;
        private System.Windows.Forms.CheckBox itemParticlesCheckBox;
        private System.Windows.Forms.CheckBox heroParticlesCheckBox;
        private System.Windows.Forms.CheckBox itemSoundsCheckBox;
        private System.Windows.Forms.CheckBox heroSoundsCheckBox;
        private System.Windows.Forms.CheckBox heroVoiceCheckBox;
        private System.Windows.Forms.CheckBox iconsCheckBox;
        private System.Windows.Forms.CheckBox soundsCheckBox;
        private System.Windows.Forms.CheckBox includeAudioCheckBox;
        private System.Windows.Forms.CheckBox pedestalCheckBox;
        private System.Windows.Forms.CheckBox replaceDefaultsCheckBox;
        private System.Windows.Forms.CheckBox replaceSharedParticlesCheckBox;
        private ThemedGroupBox outputGroupBox;
        private System.Windows.Forms.TableLayoutPanel outputTable;
        private System.Windows.Forms.Label contentFolderLabel;
        private ThemedTextBox contentFolderTextBox;
        private ThemedButton contentFolderButton;
        private System.Windows.Forms.Label gameFolderLabel;
        private ThemedTextBox gameFolderTextBox;
        private ThemedButton gameFolderButton;
        private System.Windows.Forms.TableLayoutPanel buttonsTable;
        private System.Windows.Forms.Label summaryLabel;
        private ThemedButton cancelButton;
        private ThemedButton exportButton;
        private System.Windows.Forms.Timer previewTimer;
        private System.Windows.Forms.ToolTip toolTip;
    }
}
