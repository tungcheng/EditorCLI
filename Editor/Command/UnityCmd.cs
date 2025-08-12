namespace Techies
{
    [EditorCommandClass]
    public class UnityCmd
    {
        [EditorCommand("math", "add", Description = "Adds two integer numbers.", Usage = "math add <num1> <num2>")]
        public string Add(int a, int b)
        {
            return $"{a} + {b} = {a + b}";
        }

        [EditorCommand("math", "multiply", Description = "Multiplies two float numbers.", Usage = "math multiply <num1> <num2>")]
        public string Multiply(float a, float b)
        {
            return $"{a} * {b} = {a * b}";
        }
    }
}