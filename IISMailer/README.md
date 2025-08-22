# IISMailer

To clone the repository, use the following command:

```bash
git clone --recursive https://github.com/jmalarcon/iismailer.git
```

to bring the submodules (IISHelper).

If you forget to fo it and you're just cloning the main repo it will fail to open in Visual Studio because the submodule is missing.

To fix this just use the following commands:

```bash
git submodule sync
```

or go into the `IISHelper` folder and do a `git checkout master`.
