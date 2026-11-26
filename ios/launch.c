// How ./run.sh --ios and the SDK's dotnet run start a simulator program:
// `launch <directory> <program> [args]` moves to the directory, then becomes
// the program (execvp, given a path, runs that file). simctl spawn starts
// programs in the simulated device's data directory, and the simulator has
// no shell to cd with; a simulator program that execs another keeps the
// simulator's environment, which the Mac's own programs can't give it.
int chdir(const char *path);
int execvp(const char *file, char *const argv[]);
void perror(const char *message);

int main(int argc, char **argv)
{
    if (argc < 3)
        return 2;
    if (chdir(argv[1]) != 0) {
        perror(argv[1]);
        return 127;
    }
    execvp(argv[2], argv + 2);
    perror(argv[2]);
    return 127;
}
