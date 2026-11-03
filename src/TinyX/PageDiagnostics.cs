namespace TinyX;

internal static class PageDiagnostics
{
    public static string DefaultPath => Path.Combine(AppSettings.DataDirectory, "diagnostics-current.json");

    public const string ReadyScript = """
        !!document.querySelector('[data-testid="primaryColumn"], input[type="password"], [data-testid="LoginForm_Login_Button"]')
        """;

    public const string ViewSampleScript = """
        (() => {
            const primary=document.querySelector('[data-testid="primaryColumn"]');
            const header=document.querySelector('header[role="banner"]');
            const headerStyle=header ? getComputedStyle(header) : null;
            const primaryRect=primary?.getBoundingClientRect();
            const tabs=Array.from(document.querySelectorAll('[role="tablist"]')).slice(0,8).map(element=>{
                const rect=element.getBoundingClientRect(),style=getComputedStyle(element);
                return {x:rect.x,y:rect.y,width:rect.width,height:rect.height,
                    displayed:style.display!=='none'&&style.visibility!=='hidden'&&rect.height>0,
                    inViewport:rect.height>0&&rect.bottom>0&&rect.top<innerHeight};
            });
            const tweets=Array.from(document.querySelectorAll('[data-testid="tweet"]'));
            return {scrollY,scrollX,viewportWidth:innerWidth,viewportHeight:innerHeight,
                htmlScrollTop:document.documentElement.scrollTop,bodyScrollTop:document.body.scrollTop,
                htmlScrollHeight:document.documentElement.scrollHeight,htmlClientHeight:document.documentElement.clientHeight,
                bodyScrollHeight:document.body.scrollHeight,bodyClientHeight:document.body.clientHeight,
                scrollOwner:document.scrollingElement?.tagName.toLowerCase()||null,
                tweetCount:tweets.length,visibleTweetCount:tweets.filter(element=>{
                    const rect=element.getBoundingClientRect();
                    return rect.width>0&&rect.height>0&&rect.bottom>0&&rect.top<innerHeight;
                }).length,
                primary:{x:primaryRect?.x||0,y:primaryRect?.y||0,width:primaryRect?.width||0,height:primaryRect?.height||0},
                header:{present:!!header,visibility:headerStyle?.visibility||null,
                    display:headerStyle?.display||null,position:headerStyle?.position||null,
                    width:header?.getBoundingClientRect().width||0},
                tabs};
        })()
        """;

    public const string Script = """
        (() => {
            const MAX_SCAN = 4000, MAX_NODES = 48, MAX_ANCESTORS = 32;
            const safeTestIds = new Set(['primaryColumn','sidebarColumn','tweet','tweetText','tweetPhoto',
                'videoPlayer','cellInnerDiv','virtualizedList','virtualScroller','SideNav_AccountSwitcher_Button',
                'AppTabBar_Profile_Link','SideNav_NewTweet_Button','tweetButton','tweetButtonInline',
                'toolBar','attachments','ScrollSnap-List','AppTabBar','BottomNavBar','DMDrawer',
                'GrokDrawer','BottomBar','FloatingActionButton_Tweet','LoginForm_Login_Button']);
            const safeRoles = new Set(['alert','application','article','banner','button','checkbox','combobox',
                'complementary','contentinfo','dialog','document','feed','form','generic','grid','gridcell','group',
                'heading','img','link','list','listbox','listitem','log','main','menu','menubar','menuitem',
                'navigation','none','option','presentation','progressbar','radio','radiogroup','region','row',
                'rowgroup','search','searchbox','separator','slider','spinbutton','status','switch','tab','table',
                'tablist','tabpanel','textbox','toolbar','tooltip','tree','treegrid','treeitem']);
            function testId(element) {
                const value=element.getAttribute('data-testid');
                return safeTestIds.has(value) || /^tweetTextarea_\d{1,3}$/.test(value || '') ? value : null;
            }
            function role(element) {
                const value=element.getAttribute('role');
                return safeRoles.has(value) ? value : null;
            }
            const properties = ['display','position','overflow','overflowX','overflowY','width','height',
                'minWidth','maxWidth','minHeight','maxHeight','transform','contain','flex','flexBasis',
                'flexDirection','alignItems','alignSelf','justifyContent','marginTop','marginBottom',
                'marginLeft','marginRight','paddingTop','paddingBottom','paddingLeft','paddingRight',
                'top','bottom','left','right','zIndex','visibility'];
            function node(element) {
                if (!element || element.nodeType !== 1) return null;
                const style = getComputedStyle(element), rect = element.getBoundingClientRect();
                const computed = {}, inline = {};
                for (const property of properties) {
                    computed[property] = style[property];
                    if (element.style[property]) inline[property] = element.style[property];
                }
                return {tag:element.tagName.toLowerCase(), role:role(element),
                    testId:testId(element),
                    classes:typeof element.className === 'string' ? element.className.split(/\s+/)
                        .filter(value=>/^(?:css-[a-z0-9]+|r-[a-z0-9]+|tinyx-[a-z0-9-]+)$/.test(value)).slice(0,40) : [],
                    rect:{x:rect.x,y:rect.y,width:rect.width,height:rect.height,top:rect.top,bottom:rect.bottom},
                    clientWidth:element.clientWidth,clientHeight:element.clientHeight,
                    scrollWidth:element.scrollWidth,scrollHeight:element.scrollHeight,
                    scrollTop:element.scrollTop,scrollLeft:element.scrollLeft,
                    computed,inline,
                    childStructure:Array.from(element.children).slice(0,12).map(child=>({
                        tag:child.tagName.toLowerCase(),role:role(child),
                        testId:testId(child)}))};
            }
            function ancestors(element) {
                const result=[];
                while(element && result.length<MAX_ANCESTORS){result.push(node(element));element=element.parentElement;}
                return result;
            }
            function selected(selector,limit=MAX_NODES) {
                return Array.from(document.querySelectorAll(selector)).slice(0,limit).map(node);
            }
            const primary=document.querySelector('[data-testid="primaryColumn"]');
            const firstTweet=document.querySelector('[data-testid="tweet"]');
            const composer=document.querySelector('[data-testid="tweetTextarea_0"]');
            const homeFirstTabList=primary?.querySelector('[role="tablist"]')||null;
            const composerAncestors=[];
            let candidate=composer;
            while(candidate && composerAncestors.length<MAX_ANCESTORS){
                const boundary=candidate.querySelector('article,[data-testid="tweet"],[data-testid="cellInnerDiv"],[data-testid="virtualScroller"],[role="list"],[role="region"],[role="tablist"],section,main,[role="dialog"],[aria-modal="true"]');
                composerAncestors.push({node:node(candidate),
                    firstBoundary:boundary?{tag:boundary.tagName.toLowerCase(),role:role(boundary),testId:testId(boundary)}:null,
                    homeFirstTabListContained:!!homeFirstTabList&&candidate.contains(homeFirstTabList),
                    buttons:Array.from(candidate.querySelectorAll('button,[role="button"]'))
                    .filter(button=>testId(button)!==null).slice(0,24).map(button=>({tag:button.tagName.toLowerCase(),role:role(button),
                        testId:testId(button)}))});
                candidate=candidate.parentElement;
            }
            function sheetInfo(sheet) {
                if(!sheet)return {present:false};
                try{return {present:true,disabled:sheet.disabled,ruleCount:sheet.cssRules.length};}
                catch(error){return {present:true,error:error.name};}
            }
            const tinyxStyle=document.getElementById('tinyx-content-style');
            const styleInjection={elementExists:!!tinyxStyle,connected:tinyxStyle?.isConnected===true,
                elementTag:tinyxStyle?.tagName.toLowerCase()||null,
                hasNonce:!!tinyxStyle?.nonce,
                textLength:tinyxStyle?.textContent.length||0,
                sheet:sheetInfo(tinyxStyle?.sheet),
                pageNonceStyleCount:document.querySelectorAll('style[nonce]').length,
                documentSheetCount:document.styleSheets.length,
                adoptedSheets:Array.from(document.adoptedStyleSheets||[]).slice(0,12).map(sheetInfo)};
            const scrollContainers=[];
            for(const element of Array.from(document.querySelectorAll('html,body,div,main,section')).slice(0,MAX_SCAN)){
                if(element.clientHeight<1 || element.scrollHeight<=element.clientHeight+1) continue;
                const style=getComputedStyle(element);
                if(/^(auto|scroll)$/.test(style.overflowY)) scrollContainers.push(node(element));
                if(scrollContainers.length>=MAX_NODES)break;
            }
            return {document:{readyState:document.readyState,hidden:document.hidden,
                    contentMode:document.documentElement.classList.contains('tinyx-content-mode'),
                    documentScrollMode:document.documentElement.classList.contains('tinyx-document-scroll'),
                    isXHost:/^(?:www\.)?(?:x|twitter)\.com$/.test(location.hostname),
                    isAuthenticationRoute:/^\/(?:i\/flow|login|signup|account\/access)(?:\/|$)/.test(location.pathname)},
                viewport:{width:innerWidth,height:innerHeight,scrollX,scrollY,devicePixelRatio},styleInjection,
                hasAccountMenu:!!document.querySelector('[data-testid="SideNav_AccountSwitcher_Button"],[data-testid="AppTabBar_Profile_Link"]'),
                hasLoginForm:!!document.querySelector('input[type="password"],[data-testid="LoginForm_Login_Button"]'),
                tweetCount:document.querySelectorAll('[data-testid="tweet"]').length,
                scrollingElement:node(document.scrollingElement),html:node(document.documentElement),body:node(document.body),
                primaryAncestors:ancestors(primary),tweetAncestors:ancestors(firstTweet),composerAncestors,
                homeTabsAncestors:ancestors(primary?.querySelector('[role="tablist"]')),
                scrollContainers,nav:selected('header[role="banner"]',8),
                tabs:selected('[role="tablist"],[role="tab"]',12),
                tweets:selected('[data-testid="tweet"]',24),
                virtualCells:selected('[data-testid="cellInnerDiv"],[data-testid="virtualizedList"] > div,[style*="translateY"]',32)};
        })()
        """;
}
