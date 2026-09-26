"""Run the actual editor refresh/randomize UI statements with controlled menu state.

Pass an optional MpPedCustomization.cs path to reproduce a pre-fix source failure.
The isolated baseline has no randomizer; that case runs on the combined local source.
"""
from pathlib import Path
import re
import subprocess
import sys
import tempfile

root = Path(__file__).resolve().parents[2]
source = Path(sys.argv[1]) if len(sys.argv) > 1 else root / 'vMenu/menus/MpPedCustomization.cs'
text = source.read_text(encoding='utf-8-sig')
start = text.index('else if (item == faceButton)')
start = text.index('{', start)
depth = 1
end = start + 1
while depth:
    depth += (text[end] == '{') - (text[end] == '}')
    end += 1
refresh = text[start + 1:end - 1]
expression = re.search(r'^\s*([^\n]*\.ListIndex = _facialExpressionSelection;)', text, re.M)
randomize = expression[1] if expression else ''

program = r'''
using System;
using System.Collections.Generic;
using System.Linq;
using vMenuClient;

class MenuItem { public int Index; }
class MenuListItem : MenuItem { public int ListIndex; }
class MenuSliderItem : MenuItem { public int Position; }
class Menu
{
    public List<MenuItem> Items = new();
    public List<MenuItem> GetMenuItems() => Items;
    public void RefreshIndex() { }
}
class Features { public Dictionary<int, float> features; }
class Character { public Features FaceShapeFeatures = new(); }
class Program
{
    static Character currentCharacter = new();
    static Menu faceShapeMenu = new(), createCharacterMenu = new();
    static MenuListItem faceExpressionList = new();
    static Dictionary<int, int> shapeFaceValues = new();
    static int _facialExpressionSelection = 6;
    static void OpenFaceShapes() { REFRESH }
    static void RefreshExpression() { EXPRESSION_BODY }
    static void Check(bool value, string message)
    { if (!value) throw new Exception(message); }
    static void Main()
    {
        faceShapeMenu.Items = Enumerable.Range(0, 20).Select(i => (MenuItem)new MenuSliderItem { Index = i, Position = 0 }).ToList();
        currentCharacter.FaceShapeFeatures.features = new();
        OpenFaceShapes();
        Check(faceShapeMenu.Items.Cast<MenuSliderItem>().All(s => s.Position == 10), "New character defaults without randomizing first");
        currentCharacter.FaceShapeFeatures.features = null;
        OpenFaceShapes();
        Check(faceShapeMenu.Items.Cast<MenuSliderItem>().All(s => s.Position == 10), "Legacy missing features");
        currentCharacter.FaceShapeFeatures.features = new() { [0] = .3f, [7] = -.9f, [19] = 1f };
        OpenFaceShapes();
        Check(((MenuSliderItem)faceShapeMenu.Items[0]).Position == 13, "Restored edited value");
        Check(((MenuSliderItem)faceShapeMenu.Items[7]).Position == 1, "Negative saved value rounds correctly");
        Check(((MenuSliderItem)faceShapeMenu.Items[19]).Position == 20, "Neck feature restored");
        currentCharacter.FaceShapeFeatures.features[0] = -.5f;
        OpenFaceShapes();
        Check(((MenuSliderItem)faceShapeMenu.Items[0]).Position == 5, "Reopen reflects latest edit, not a randomizer cache");
        currentCharacter.FaceShapeFeatures.features = new() { [0] = 99, [1] = -99, [2] = float.NaN };
        OpenFaceShapes();
        Check(((MenuSliderItem)faceShapeMenu.Items[0]).Position == 20 && ((MenuSliderItem)faceShapeMenu.Items[1]).Position == 0
            && ((MenuSliderItem)faceShapeMenu.Items[2]).Position == 10, "Malformed feature values bounded");
        currentCharacter = new();
        OpenFaceShapes();
        Check(faceShapeMenu.Items.Cast<MenuSliderItem>().All(s => s.Position == 10), "Next character does not inherit previous slider state");
        if (HAS_RANDOMIZER)
        {
            createCharacterMenu.Items = Enumerable.Range(0, 12).Select(i => new MenuItem { Index = i }).ToList();
            createCharacterMenu.Items[10] = faceExpressionList;
            RefreshExpression();
            Check(faceExpressionList.ListIndex == 6, "Randomizer updates the expression control, not slot 7");
            Console.WriteLine("PASS: randomizer expression with a button in slot 7");
        }
        Console.WriteLine("PASS: new/legacy/sparse/edited/malformed face features and character switching");
    }
}
'''.replace('REFRESH', refresh).replace('EXPRESSION_BODY', randomize).replace('HAS_RANDOMIZER', str(bool(randomize)).lower())

with tempfile.TemporaryDirectory(prefix='vmenu-editor-errors-') as folder:
    work = Path(folder)
    (work / 'Tests.csproj').write_text('''<Project Sdk="Microsoft.NET.Sdk">
<PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework></PropertyGroup>
</Project>''', encoding='utf-8')
    (work / 'Program.cs').write_text(program, encoding='utf-8')
    (work / 'FaceFeatureValues.cs').write_bytes((root / 'vMenu/FaceFeatureValues.cs').read_bytes())
    subprocess.run(['dotnet', 'run', '--project', str(work / 'Tests.csproj'),
                    '--configuration', 'Release', '--verbosity', 'quiet'], check=True)
