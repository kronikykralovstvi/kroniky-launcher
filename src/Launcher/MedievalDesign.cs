using System.Drawing.Drawing2D;
using System.Text.Json;
using KronikyLauncher.Mods;

namespace KronikyLauncher;

internal sealed partial class LauncherForm
{
    private const string Website = "https://kronikykralovstvi.cz/";
    private const string Discord = "https://discord.gg/uYYEDDFPcF";
    private readonly List<(Control control, Rectangle bounds, float fontSize)> designControls=[];
    private readonly NewsCard[] newsCards=[new(),new()];
    private readonly Label releaseLabel=new();
    private readonly System.Windows.Forms.Timer newsTimer=new(){Interval=300000};
    private bool loadingNews;
    private DesignSurface surface=null!;

    private void InitializeDesign()
    {
        Text="Kroniky království";AutoScaleMode=AutoScaleMode.None;FormBorderStyle=FormBorderStyle.None;
        MinimumSize=new Size(1000,650);ClientSize=new Size(1269,794);StartPosition=FormStartPosition.CenterScreen;
        BackColor=Color.FromArgb(6,17,29);ForeColor=Color.FromArgb(244,224,182);
        using var stream=typeof(LauncherForm).Assembly.GetManifestResourceStream("KronikyKralovstviLauncher.launcher-design.png")!;
        using var source=Image.FromStream(stream);surface=new DesignSurface(new Bitmap(source)){Dock=DockStyle.Fill};Controls.Add(surface);
        Place(Label("KRONIKY KRÁLOVSTVÍ",ContentAlignment.MiddleCenter),360,5,870,40,26);
        AddButton("—",1440,7,43,34,()=>WindowState=FormWindowState.Minimized,23);
        AddButton("□",1486,7,43,34,()=>WindowState=WindowState==FormWindowState.Maximized?FormWindowState.Normal:FormWindowState.Maximized,23);
        AddButton("×",1532,7,43,34,()=>Close(),27);
        Place(Label("Jméno hráče"),96,632,510,45,30);
        playerName.Dock=DockStyle.None;playerName.BackColor=Color.FromArgb(15,29,39);playerName.ForeColor=ForeColor;
        playerName.BorderStyle=BorderStyle.None;playerName.MaxLength=24;playerName.PlaceholderText="Zadej své jméno";
        Place(playerName,125,694,465,42,25);
        play.Text="VSTOUPIT DO HRY";Place(play,94,764,532,88,32);
        Place(Label("Před vstupem ověříme potřebné mody.",ContentAlignment.MiddleCenter),90,854,540,24,15);
        Place(Label("Novinky"),720,234,760,57,42);
        Place(newsCards[0],718,303,820,171,20);Place(newsCards[1],718,487,820,171,20);
        Place(Label("Aktualizace"),718,680,780,54,38);
        update.Text="Zkontrolovat a opravit mody";Place(update,720,747,500,54,23);
        AddButton("Přehled změn  ›",1233,747,305,54,()=>OpenLink(Website+"novinky/"),22);
        status.Text="Připravuji ověřené aktualizace…";status.TextAlign=ContentAlignment.MiddleLeft;status.ForeColor=ForeColor;
        Place(status,722,810,813,43,17);
        progress.Style=ProgressBarStyle.Marquee;Place(progress,722,855,813,7,12);
        releaseLabel.Text="Launcher "+AppVersion+" • podepsané aktualizace";Place(releaseLabel,722,875,813,22,14);
        AddButton("◎  Oficiální web",70,925,275,43,()=>OpenLink(Website),25);
        AddButton("Discord",363,925,220,43,()=>OpenLink(Discord),25);
        gameFolder.Text="Nastavení hry";Place(gameFolder,615,928,260,39,18);
        AddButton("Ochrana a obnova",900,928,270,39,ShowSecurity,18);
        Place(Label("YARD141",ContentAlignment.MiddleRight),1300,925,220,45,25);
        version.Visible=false;
        FormClosing+=(_,_)=>{newsTimer.Stop();if(!string.IsNullOrWhiteSpace(playerName.Text)){Directory.CreateDirectory(root);File.WriteAllText(playerNameFile,playerName.Text.Trim());}};
        newsTimer.Tick+=async(_,_)=>await RefreshNews();newsTimer.Start();
        Resize+=(_,_)=>LayoutDesign();LayoutDesign();
    }
    private static Label Label(string text,ContentAlignment align=ContentAlignment.MiddleLeft)=>new(){Text=text,TextAlign=align,BackColor=Color.FromArgb(7,22,35),ForeColor=Color.FromArgb(246,222,179)};
    private void Place(Control c,int x,int y,int w,int h,float font)
    {
        c.Dock=DockStyle.None;c.Anchor=AnchorStyles.Top|AnchorStyles.Left;c.TabIndex=designControls.Count;
        c.AccessibleName=c==playerName?"Jméno hráče":null;
        surface.Controls.Add(c);designControls.Add((c,new Rectangle(x,y,w,h),font));
    }
    private void AddButton(string text,int x,int y,int w,int h,Action action,float font)
    {var button=new RoyalButton{Text=text};button.Click+=(_,_)=>action();Place(button,x,y,w,h,font);}
    private void LayoutDesign()
    {
        if(surface is null)return;float sx=ClientSize.Width/1586f,sy=ClientSize.Height/992f,scale=Math.Min(sx,sy);
        foreach(var (control,bounds,font) in designControls)
        {
            control.Bounds=new Rectangle((int)(bounds.X*sx),(int)(bounds.Y*sy),(int)(bounds.Width*sx),(int)(bounds.Height*sy));
            var previous=control.Tag as Font;control.Font=new Font(control is TextBox?"Segoe UI":"Georgia",Math.Max(8,font*scale),control==play?FontStyle.Bold:FontStyle.Regular,GraphicsUnit.Pixel);
            control.Tag=control.Font;previous?.Dispose();
        }
        surface.Invalidate();
    }
    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if(m.Msg==0x84 && m.Result==(IntPtr)1)
        {
            var p=PointToClient(new Point(unchecked((short)(long)m.LParam),unchecked((short)((long)m.LParam>>16))));
            if(p.Y<ClientSize.Height*46/992 && p.X<ClientSize.Width*1435/1586)m.Result=(IntPtr)2;
        }
    }
    private void OpenLink(string url)
    {
        if(!Uri.TryCreate(url,UriKind.Absolute,out var uri)||uri.Scheme!="https"||uri.UserInfo.Length>0||uri.Port!=443 ||
          !(uri.Host=="kronikykralovstvi.cz" || url==Discord || (uri.Host=="github.com"&&uri.AbsolutePath.StartsWith("/kronikykralovstvi/kroniky-launcher/",StringComparison.Ordinal))))
            return;
        try{System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri.AbsoluteUri){UseShellExecute=true});}
        catch(Exception ex){MessageBox.Show(this,"Odkaz se nepodařilo otevřít: "+ex.Message,"Kroniky království");}
    }
    private void ShowSecurity()=>MessageBox.Show(this,
        "Aktualizace jsou ověřované podpisem Kronik a kontrolními součty. Neplatné vydání se nenainstaluje.\n\nPři chybě nebo přerušení instalace se automaticky obnoví původní soubory. Zálohy jsou ve složce hry .kroniky-mods.\n\nPokud kontrola neprojde, zkus aktualizaci znovu nebo kontaktuj administraci přes Discord. Launcher nemá skrytý vzdálený přístup.","Ochrana a obnova",MessageBoxButtons.OK,MessageBoxIcon.Information);
    private async Task RefreshNews()
    {
        if(loadingNews||IsDisposed)return;loadingNews=true;
        try
        {
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var data=await ReleaseTrust.DownloadSmall(http,Website+"api/news",2*1024*1024,timeout.Token);
            using var doc=JsonDocument.Parse(data);var posts=doc.RootElement.GetProperty("news").EnumerateArray().Take(2).ToArray();
            for(int i=0;i<newsCards.Length;i++)
            {
                if(i>=posts.Length){newsCards[i].Headline="Z království";newsCards[i].Summary="Aktuální dění sleduj na webu a Discordu.";continue;}
                var post=posts[i];var card=newsCards[i];string title=post.GetProperty("title").GetString()??"Novinky";
                card.Headline=title.Length>120?title[..120]:title;string summary=post.GetProperty("body").GetString()??"";
                card.Summary=summary.Length>240?summary[..240]+"…":summary;
                card.ArticleUrl=Website+"novinky/";card.AccessibleName=card.Headline;
                if(post.TryGetProperty("banner",out var banner)&&banner.GetString() is string path && path.StartsWith("/assets/",StringComparison.Ordinal)&&!path.Contains(".."))
                {
                    try
                    {
                        var imageBytes=await ReleaseTrust.DownloadSmall(http,Website.TrimEnd('/')+path,5*1024*1024,timeout.Token);
                        using var memory=new MemoryStream(imageBytes);using var image=Image.FromStream(memory);
                        if(image.Width<=4096&&image.Height<=4096){var old=card.Art;card.Art=new Bitmap(image);old?.Dispose();}
                    }catch { /* Text and link remain usable if the banner fails. */ }
                }
                card.Invalidate();
            }
        }
        catch
        {
            for(int i=0;i<newsCards.Length;i++)if(newsCards[i].ArticleUrl is null){newsCards[i].Headline=i==0?"Novinky z království":"Komunita a pomoc";newsCards[i].Summary="Novinky teď nelze načíst. Kliknutím otevřeš web.";newsCards[i].ArticleUrl=Website+"novinky/";newsCards[i].Invalidate();}
        }
        finally{loadingNews=false;}
    }
}

internal sealed class DesignSurface:Panel
{
    private readonly Image background;
    public DesignSurface(Image background){this.background=background;DoubleBuffered=true;ResizeRedraw=true;}
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var g=e.Graphics;g.DrawImage(background,ClientRectangle);float sx=Width/1586f,sy=Height/992f;
        using var dark=new SolidBrush(Color.FromArgb(7,22,35));using var gold=new Pen(Color.FromArgb(210,164,75),Math.Max(1,2*sx));
        foreach(var rect in new[]{new RectangleF(690,220,868,685),new RectangleF(62,606,593,278),new RectangleF(100,680,516,60),new RectangleF(16,916,1554,58),new RectangleF(12,5,1564,39)})
        {var r=new RectangleF(rect.X*sx,rect.Y*sy,rect.Width*sx,rect.Height*sy);g.FillRectangle(dark,r);g.DrawRectangle(gold,r.X,r.Y,r.Width,r.Height);}
        g.DrawRectangle(gold,2,2,Width-5,Height-5);
    }
    protected override void Dispose(bool disposing){if(disposing)background.Dispose();base.Dispose(disposing);}
}
internal class RoyalButton:Button
{
    [System.ComponentModel.DefaultValue(false)]
    public bool Gold {get;set;}
    public RoyalButton(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);Cursor=Cursors.Hand;FlatStyle=FlatStyle.Flat;UseVisualStyleBackColor=false;}
    protected override void OnPaint(PaintEventArgs e)
    {
        bool hover=ClientRectangle.Contains(PointToClient(Cursor.Position));var r=ClientRectangle;if(r.Width<2||r.Height<2)return;
        using var fill=new LinearGradientBrush(r,Gold?Color.FromArgb(245,212,128):Color.FromArgb(18,39,55),Gold?Color.FromArgb(178,119,38):Color.FromArgb(5,18,29),90f);
        e.Graphics.FillRectangle(fill,r);using var border=new Pen(hover?Color.FromArgb(255,221,145):Color.FromArgb(198,153,73),Focused?3:1);
        e.Graphics.DrawRectangle(border,1,1,r.Width-3,r.Height-3);
        TextRenderer.DrawText(e.Graphics,Text,Font,r,Enabled?(Gold?Color.FromArgb(23,18,11):Color.FromArgb(246,222,179)):Color.FromArgb(140,135,120),TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);
    }
}
internal sealed class NewsCard:RoyalButton
{
    public string Headline="Načítám novinky…",Summary="Aktuality z webu Kroniky Království";
    public string? ArticleUrl;
    public Image? Art;
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);int pad=Math.Max(8,Width/70),imageWidth=Art is null?0:Width*35/100;
        if(Art is not null)e.Graphics.DrawImage(Art,new Rectangle(pad,pad,imageWidth,Height-pad*2));
        var title=new Rectangle(pad*2+imageWidth,pad,Width-imageWidth-pad*3,Height*43/100);
        using var bold=new Font(Font.FontFamily,Font.Size*1.1f,FontStyle.Bold,GraphicsUnit.Pixel);
        TextRenderer.DrawText(e.Graphics,Headline,bold,title,Color.FromArgb(246,222,179),TextFormatFlags.WordBreak|TextFormatFlags.EndEllipsis);
        using var body=new Font("Segoe UI",Math.Max(10,Font.Size*.83f),FontStyle.Regular,GraphicsUnit.Pixel);
        TextRenderer.DrawText(e.Graphics,Summary,body,new Rectangle(title.X,Height*46/100,title.Width,Height*49/100-pad),Color.FromArgb(221,218,207),TextFormatFlags.WordBreak|TextFormatFlags.EndEllipsis);
    }
    protected override void Dispose(bool disposing){if(disposing)Art?.Dispose();base.Dispose(disposing);}
}
