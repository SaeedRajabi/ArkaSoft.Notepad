import io

p = "MainWindow.xaml"
s = io.open(p, encoding="utf-8").read()

# 1) rows: insert format bar row
old_rows = '''        <Grid.RowDefinitions>
            <RowDefinition Height="40"/>
            <RowDefinition Height="40"/>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>'''
new_rows = '''        <Grid.RowDefinitions>
            <RowDefinition Height="40"/>
            <RowDefinition Height="40"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>'''
assert old_rows in s, "rows"
s = s.replace(old_rows, new_rows)

# 2) format toolbar row + render pane + shift rows
old_editor = '''        <!-- LTR container: keeps margins/alignment physical so the editor docks
             against the sidebar correctly in both languages -->
        <Grid Grid.Row="2" FlowDirection="LeftToRight">
            <Grid x:Name="EditorArea" FlowDirection="LeftToRight">
                <ContentPresenter x:Name="EditorHost"/>
                <controls:FindReplacePanel x:Name="FindPanel"
                                           HorizontalAlignment="Right"
                                           VerticalAlignment="Top"
                                           Visibility="Collapsed"/>
            </Grid>'''
new_editor = '''        <!-- ============ Formatting toolbar ============ -->
        <Border Grid.Row="2" x:Name="FormatBar"
                Background="{DynamicResource C.Window}"
                BorderBrush="{DynamicResource C.Line}" BorderThickness="0,1,0,1">
            <WrapPanel Margin="6,3" VerticalAlignment="Center">
                <Button x:Name="BoldButton" Style="{StaticResource Btn.Icon}" Width="28" Height="26"
                        FontFamily="{DynamicResource F.Icons}" FontSize="12" Content="&#xE734;"
                        ToolTip="{DynamicResource T.Bold}" Click="BoldButton_Click"/>
                <Button x:Name="ItalicButton" Style="{StaticResource Btn.Icon}" Width="28" Height="26"
                        FontFamily="{DynamicResource F.Icons}" FontSize="12" Content="&#xE735;"
                        ToolTip="{DynamicResource T.Italic}" Click="ItalicButton_Click"/>
                <Button x:Name="UnderlineButton" Style="{StaticResource Btn.Icon}" Width="28" Height="26"
                        FontFamily="{DynamicResource F.Icons}" FontSize="12" Content="&#xDE3E;"
                        ToolTip="{DynamicResource T.Underline2}" Click="UnderlineButton_Click"/>
                <Border Width="1" Height="20" Background="{DynamicResource C.Line}" Margin="4,0"/>
                <ComboBox x:Name="FontFamilyBox" Width="150" Height="26" Margin="2,0,2,0"
                          ToolTip="{DynamicResource T.FontFamilyTip}"
                          SelectionChanged="FontFamilyBox_SelectionChanged"
                          DropDownClosed="FontFamilyBox_DropDownClosed"/>
                <ComboBox x:Name="FontSizeBox" Width="62" Height="26" Margin="2,0,2,0" IsEditable="True"
                          ToolTip="{DynamicResource T.FontSizeTip}"
                          SelectionChanged="FontSizeBox_SelectionChanged"
                          KeyDown="FontSizeBox_KeyDown"/>
                <Border Width="1" Height="20" Background="{DynamicResource C.Line}" Margin="4,0"/>
                <Button Style="{StaticResource Btn.Icon}" Width="28" Height="26"
                        FontFamily="{DynamicResource F.Icons}" FontSize="12" Content="&#xE790;"
                        ToolTip="{DynamicResource T.TextColor}" Click="TextColorButton_Click"/>
                <Button Style="{StaticResource Btn.Icon}" Width="28" Height="26"
                        FontFamily="{DynamicResource F.Icons}" FontSize="12" Content="&#xE7E6;"
                        ToolTip="{DynamicResource T.Highlight}" Click="HighlightButton_Click"/>
                <Border Width="1" Height="20" Background="{DynamicResource C.Line}" Margin="4,0"/>
                <Button Width="30" Height="26" Content="H1" FontFamily="{DynamicResource F.Ui}"
                        FontSize="12" FontWeight="Bold" Margin="2,0"
                        ToolTip="{DynamicResource T.Heading1}" Click="HeadingButton_Click" Tag="H1"/>
                <Button Width="30" Height="26" Content="H2" FontFamily="{DynamicResource F.Ui}"
                        FontSize="12" FontWeight="Bold" Margin="2,0"
                        ToolTip="{DynamicResource T.Heading2}" Click="HeadingButton_Click" Tag="H2"/>
                <Button Width="30" Height="26" Content="H3" FontFamily="{DynamicResource F.Ui}"
                        FontSize="12" FontWeight="SemiBold" Margin="2,0"
                        ToolTip="{DynamicResource T.Heading3}" Click="HeadingButton_Click" Tag="H3"/>
                <Button Width="34" Height="26" Content="Aa" FontFamily="{DynamicResource F.Ui}"
                        FontSize="12" Margin="2,0"
                        ToolTip="{DynamicResource T.NormalText}" Click="HeadingButton_Click" Tag="Normal"/>
                <Border Width="1" Height="20" Background="{DynamicResource C.Line}" Margin="4,0"/>
                <Button Style="{StaticResource Btn.Icon}" Width="28" Height="26"
                        FontFamily="{DynamicResource F.Icons}" FontSize="12" Content="&#xE8FD;"
                        ToolTip="{DynamicResource T.BulletList}" Click="BulletListButton_Click"/>
                <Button Style="{StaticResource Btn.Icon}" Width="28" Height="26"
                        FontFamily="{DynamicResource F.Icons}" FontSize="12" Content="&#xE8FE;"
                        ToolTip="{DynamicResource T.NumberedList}" Click="NumberListButton_Click"/>
                <Button Style="{StaticResource Btn.Icon}" Width="28" Height="26"
                        FontFamily="{DynamicResource F.Icons}" FontSize="12" Content="&#xE91B;"
                        ToolTip="{DynamicResource T.InsertImage}" Click="InsertImageButton_Click"/>
                <Button Style="{StaticResource Btn.Icon}" Width="28" Height="26"
                        FontFamily="{DynamicResource F.Icons}" FontSize="12" Content="&#xE714;"
                        ToolTip="{DynamicResource T.InsertVideo}" Click="InsertVideoButton_Click"/>
                <Button Style="{StaticResource Btn.Icon}" Width="28" Height="26"
                        FontFamily="{DynamicResource F.Icons}" FontSize="12" Content="&#xE751;"
                        ToolTip="{DynamicResource T.ClearFormat}" Click="ClearFormatButton_Click"/>
            </WrapPanel>
        </Border>

        <!-- LTR container: keeps margins/alignment physical so the editor docks
             against the sidebar correctly in both languages -->
        <Grid Grid.Row="3" FlowDirection="LeftToRight">
            <Grid x:Name="EditorArea" FlowDirection="LeftToRight">
                <ContentPresenter x:Name="EditorHost"/>
                <Grid x:Name="RenderPane" Visibility="Collapsed"
                      Background="{DynamicResource C.Surface}" Panel.ZIndex="1">
                    <Border x:Name="WebViewHost"/>
                    <TextBlock x:Name="RenderMessage" Visibility="Collapsed"
                               TextWrapping="Wrap" Margin="24"
                               VerticalAlignment="Center" HorizontalAlignment="Center"
                               FontFamily="{DynamicResource F.Ui}" FontSize="13"
                               Foreground="{DynamicResource C.Text2}"/>
                </Grid>
                <controls:FindReplacePanel x:Name="FindPanel"
                                           HorizontalAlignment="Right"
                                           VerticalAlignment="Top"
                                           Visibility="Collapsed"/>
            </Grid>'''
assert old_editor in s, "editor block"
s = s.replace(old_editor, new_editor)

# 3) status bar row index 3 -> 4
old_status = '<Border Grid.Row="3" x:Name="StatusBar"'
assert old_status in s, "status"
s = s.replace(old_status, '<Border Grid.Row="4" x:Name="StatusBar"')

# 4) View menu render item before RestoreSession
old_menu = '''                <MenuItem Header="{DynamicResource T.RestoreSession}" x:Name="RestoreSessionMenuItem"'''
assert old_menu in s, "menu"
s = s.replace(old_menu, '''                <MenuItem Header="{DynamicResource T.RenderHtml}" x:Name="RenderHtmlMenuItem"
                          IsCheckable="True" Click="RenderHtmlMenuItem_Click"/>
''' + old_menu)

io.open(p, "w", encoding="utf-8", newline="").write(s)
print("XAML patched OK")
