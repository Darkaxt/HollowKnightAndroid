using HkPauseContracts;
using Xunit;

[Collection("HollowKnightPauseOwners")]
public class HollowKnightFiveRouteReviewFixTests
{
    static void Near(float expected,float actual) => Assert.InRange(actual,expected-.02f,expected+.02f);
    static HKDualScreen SwitchFixture(int from)
    {
        var f=new HKDualScreen(); f.tab.cur=f.tab.built=from;f.tab.tap=from;
        f.cfg.compTab=from; f.paneClone=from==0 ? null : f.Route(from); f.tab.lastCfg=from; f.cfg.compTabSlide=1;f.cfg.compTabSlideTime=1;
        Time.unscaledDeltaTime=.1f;
        f.compRoot.position=new Vector3(20000,20000,0);
        f.FramePane(f.compRoot.position); return f;
    }
    static Vector2 Project(HKDualScreen f,Vector3 point) => new(
        (point.x-f.attrCam.transform.position.x)/f.attrCam.orthographicSize,
        (point.y-f.attrCam.transform.position.y)/f.attrCam.orthographicSize);

    [Fact]
    public void Real_switch_preserves_outgoing_projected_size_and_vertical_anchor_all_25_pairs()
    {
        for(int from=0;from<5;from++) for(int to=0;to<5;to++)
        {
            var f=SwitchFixture(from); var outgoing=f.Route(from);
            outgoing.transform.localPosition=new Vector3(2,3,0);
            outgoing.transform.localScale=new Vector3(1.2f,.8f,1);
            Vector3 Point() => outgoing.transform.TransformPoint(new Vector3(.7f,.6f,0));
            var before=Project(f,Point());float sizeBefore=outgoing.transform.lossyScale.y/f.attrCam.orthographicSize;
            f.tab.tap=to; f.Step(false);
            if(from==to) { Assert.Null(f.slideOutClone);continue; }
            Assert.Same(outgoing,f.slideOutClone);
            Near(before.y,Project(f,Point()).y);
            float ease=1-MathF.Pow(1-f.slideT,3);
            Near(before.x-f.slideDir*f.attrCam.aspect*2.15f*ease,Project(f,Point()).x);
            Near(sizeBefore,outgoing.transform.lossyScale.y/f.attrCam.orthographicSize);
            var anchor=Project(f,Point()).y;float targetOrtho=f.attrCam.orthographicSize;
            f.Step(false);Near(anchor,Project(f,Point()).y);Near(targetOrtho,f.attrCam.orthographicSize);
            Near(sizeBefore,outgoing.transform.lossyScale.y/f.attrCam.orthographicSize);
            Assert.True(f.attrCam.orthographic); Assert.True(f.attrCam.enabled);
            Assert.True(f.hudCam2.enabled);
            f.StowStep();
            Near(2,outgoing.transform.localPosition.x);Near(3,outgoing.transform.localPosition.y);
            Near(1.2f,outgoing.transform.localScale.x);Near(.8f,outgoing.transform.localScale.y);
        }
    }

    [Fact]
    public void Mid_slide_owner_teardown_restores_original_transform_before_retirement()
    {
        var f=SwitchFixture(1);var outgoing=f.Route(1);
        outgoing.transform.localPosition=new Vector3(2,3,0);outgoing.transform.localScale=new Vector3(1.2f,.8f,1);
        f.tab.tap=3;f.Step(false);Assert.Same(outgoing,f.slideOutClone);
        f.HudFaded=true;f.MenuStep();Near(2,outgoing.transform.localPosition.x);Near(3,outgoing.transform.localPosition.y);
        Near(1.2f,outgoing.transform.localScale.x);Near(.8f,outgoing.transform.localScale.y);
    }

    [Theory]
    [InlineData(1240,1080)] [InlineData(800,720)] [InlineData(1600,1200)]
    public void Real_journal_label_clips_have_disjoint_bounded_prose_rectangles(int w,int h)
    {
        var f=new HKDualScreen { BOTTOM_W=w,BOTTOM_H=h }; f.journalRecords.Add(new());
        f.journalVisible.Add(0);f.journalSelected=0;f.LayoutStep(3);
        var d=f.journalDescription.ClipRect;var n=f.journalNotes.ClipRect;
        Assert.True(d.y+d.height<=n.y); Assert.True(n.y+n.height<=f.LowerGeometry().TabTop-16+.01f);
        Assert.True(d.height>0 && n.height>0);
        foreach(var label in new[]{f.journalDescription,f.journalNotes})
        {
            var min=label.Root.TransformPoint(new Vector3(label.Renderer.Clip.x,label.Renderer.Clip.y,0));
            var max=label.Root.TransformPoint(new Vector3(label.Renderer.Clip.z,label.Renderer.Clip.w,0));
            Near(label.ClipRect.width,max.x-min.x); Near(label.ClipRect.height,max.y-min.y);
        }
    }

    [Fact]
    public void Constructed_equipped_charms_are_explicit_inventory_chrome_above_body_masks()
    {
        var f=new HKDualScreen();f.EquipBuildStep();
        Assert.Equal(11,f.equipCharmSRs.Count);
        Assert.All(f.equipCharmSRs,r=> { Assert.Equal("Inventory",r.sortingLayerName);Assert.True(r.sortingOrder>10000); });
    }

    [Theory]
    [InlineData(1240,1080,8.71f)] [InlineData(800,720,360f)]
    public void Actual_header_path_restores_measured_container_before_mesh(int w,int h,float ortho)
    {
        var f=new HKDualScreen { BOTTOM_W=w,BOTTOM_H=h };f.attrCam.orthographicSize=ortho;
        f.HeaderStep(3);
        Near(w-40,f.shellTitle.Container.size.x*f.shellTitle.UnitScale);
        Assert.True(f.shellTitle.Container.size.y*f.shellTitle.UnitScale>=52);
        Assert.True(f.shellTitle.Tmp.LastMeshSize.x>1);
        Assert.Equal(-32767,f.shellTitle.Renderer.Clip.x);
        Assert.Equal(32767,f.shellTitle.Renderer.Clip.z);
    }

    [Fact]
    public void Actual_copy_calibrates_padded_donor_ink_and_disables_autosizing()
    {
        var f=new HKDualScreen();var donor=f.TextDonor(new Vector3(3,4,1),true);
        var label=f.CopyLabelStep(donor,30);
        Assert.NotNull(label);Assert.False(label.Tmp.enableAutoSizing);
        Near(30,label.Tmp.textBounds.size.y*label.UnitScale);
        f.LabelStep(label,new string('M',300),new Rect(890,322,330,250));
        Assert.False(label.Tmp.enableAutoSizing); Near(40,label.Tmp.fontSize);
    }

    [Fact]
    public void Config_reload_holds_equipment_dirty_while_paused_then_consumes_once_in_inventory()
    {
        var f=SwitchFixture(1);f.ConfigReloadStep();
        f.Step(true);Assert.Equal(0,f.EquipmentReassertions);
        f.Step(false);Assert.Equal(1,f.EquipmentReassertions);
        f.Step(false);Assert.Equal(1,f.EquipmentReassertions);
    }

    [Fact]
    public void Cold_frame_and_retry_construct_only_one_no_map_label()
    {
        var f=new HKDualScreen();f.frameRoot=null;f.FrameBuildStep();
        Assert.Equal(1,f.NoMapBuilds); f.FrameBuildStep();Assert.Equal(1,f.NoMapBuilds);
        f.RetryNoMapStep(true);Assert.Equal(2,f.NoMapBuilds);
        f.RetryNoMapStep();Assert.Equal(2,f.NoMapBuilds);
    }

    [Fact]
    public void Reset_action_is_built_available_in_current_map_and_dispatches_without_marker_writes()
    {
        var f=SwitchFixture(0);f.BuildActionsStep(); Assert.NotNull(f.mapResetAction);
        f.mapAnyAvailable=f.mapAvailable=f.mapContentVisible=true; f.mapGm=new GameMap();
        f.PositionActionsStep();Assert.True(f.mapResetAction.Root.gameObject.activeSelf);
        Assert.True(f.MapActionTap(f.mapResetAction.Hit.center)); Assert.Equal(1,f.AnimatedResets);Assert.Equal(0,f.MarkerWrites);
        f.mapMarkerMode=true;f.PositionActionsStep(); Assert.False(f.mapResetAction.Root.gameObject.activeSelf);
        f.mapContentVisible=false;f.PositionActionsStep();Assert.False(f.mapResetAction.Root.gameObject.activeSelf);
    }

    [Fact]
    public void Chooser_drag_and_portrait_drag_cannot_steal_selected_detail_offset()
    {
        var f=SwitchFixture(3);
        for(int i=0;i<30;i++) { f.journalRecords.Add(new());f.journalVisible.Add(i); }
        f.journalSelected=0;f.LayoutStep(3);
        f.transport.contacts=1;f.transport.TouchX=.1f;f.transport.TouchY=.4f;f.transport.T0Y=.2f;f.transport.TapSequence++;
        f.TouchStep();Assert.Equal(1,f.journalScrollRow);Near(0,f.journalDescription.ScrollOffset);
        f.transport.TouchX=.5f;f.transport.TapSequence++;f.TouchStep();Assert.Equal(1,f.journalScrollRow);
    }

    [Fact]
    public void Selected_detail_offset_resets_on_language_geometry_and_owner_retirement()
    {
        var f=SwitchFixture(3);f.journalRecords.Add(new());
        HkPauseContracts.PlayerData.instance.hasJournal=true;HkPauseContracts.PlayerData.instance.Bools["killedCrawler"]=true;
        f.journalSelected=0;f.journalDescription.Tmp.InkHeight=1000;f.JournalDataStep(true);
        f.journalDescription.ScrollOffset=100;Time.frameCount+=30;f.SupplementaryStep();Near(0,f.journalDescription.ScrollOffset);
        f.journalDescription.ScrollOffset=100;f.BOTTOM_H=720;Time.frameCount+=30;f.SupplementaryStep();Near(0,f.journalDescription.ScrollOffset);
        f.journalDescription.ScrollOffset=100;
        int language=TeamCherry.Localization.Language.Code;
        try { TeamCherry.Localization.Language.Code=language+1;Time.frameCount+=30;f.SupplementaryStep();Near(0,f.journalDescription.ScrollOffset); }
        finally { TeamCherry.Localization.Language.Code=language; }
        f.HudFaded=true;f.MenuStep();Assert.Null(f.journalDescription);
    }

    [Fact]
    public void Journal_detail_drag_scrolls_ink_inside_fixed_clip_without_scrolling_chooser()
    {
        var f=SwitchFixture(3);f.journalRecords.Add(new());f.journalVisible.Add(0);f.journalSelected=0;
        f.journalDescription.Tmp.InkHeight=1000; f.journalNotes.Tmp.InkHeight=900;
        f.LayoutStep(3);var clip=f.journalDescription.ClipRect;
        f.transport.contacts=1;f.transport.TouchX=.9f;f.transport.TouchY=.4f;f.transport.T0Y=.2f;f.transport.TapSequence++;
        f.TouchStep();Assert.Equal(0,f.journalScrollRow);Assert.True(f.journalDescription.ScrollOffset>0);
        Assert.Equal(clip.y,f.journalDescription.ClipRect.y);
        for(int i=0;i<30;i++) f.ScrollStep(-.2f);
        Near(f.journalDescription.ScrollMax,f.journalDescription.ScrollOffset);
        var lastInk=f.journalDescription.Root.TransformPoint(f.journalDescription.Tmp.textBounds.min);
        Near(f.compRoot.position.y+f.BOTTOM_H/2f-(clip.y+clip.height),lastInk.y);
        for(int i=0;i<30;i++) f.ScrollStep(.2f);
        Near(0,f.journalDescription.ScrollOffset);
        f.journalDescription.ScrollOffset=100; f.SelectionTap(3,.05f,.28f);
        Near(0,f.journalDescription.ScrollOffset);
    }
}
